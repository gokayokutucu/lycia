// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lycia.Scheduling;

/// <summary>Auditable, lease-protected cleanup worker for proven Lycia-owned dynamic scheduling resources.</summary>
public sealed class VacuumWorker(
    ISchedulingResourceRegistry registry,
    IScheduleStore scheduleStore,
    ITopologyManifestRegistry manifests,
    IVacuumLeaseManager leases,
    IEnumerable<ISchedulingResourceManager> managers,
    ISchedulingClock clock,
    IOptions<SchedulingOptions> options,
    ILogger<VacuumWorker> logger) : BackgroundService
{
    private readonly string _owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    /// <summary>Runs one scheduling-resource vacuum pass and returns the number of deleted resources.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value.Vacuum.SchedulingResources;
        if (!settings.Enabled) return 0;
        var deleted = 0;
        foreach (var manager in managers)
            deleted += await RunManagerAsync(manager, settings, cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Vacuum.SchedulingResources.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Lycia scheduling-resource vacuum pass failed"); }
            await Task.Delay(options.Value.Vacuum.SchedulingResources.Interval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<int> RunManagerAsync(ISchedulingResourceManager manager,
        SchedulingResourceVacuumOptions settings, CancellationToken cancellationToken)
    {
        var scope = "lycia:vacuum:scheduling:" + manager.TransportName;
        var fence = await leases.TryAcquireAsync(scope, _owner, clock.UtcNow, settings.Interval,
            cancellationToken).ConfigureAwait(false);
        if (!fence.HasValue) return 0;
        try
        {
            var activeManifests = await manifests.GetActiveAsync(clock.UtcNow, TimeSpan.FromMinutes(2), cancellationToken)
                .ConfigureAwait(false);
            var resources = await registry.ListCandidatesAsync(settings.BatchSize, cancellationToken).ConfigureAwait(false);
            var deleted = 0;
            foreach (var resource in resources.Where(resource =>
                         string.Equals(resource.Transport, manager.TransportName, StringComparison.OrdinalIgnoreCase)))
            {
                var state = await manager.InspectAsync(resource, cancellationToken).ConfigureAwait(false);
                if (state.ConsumerCount.GetValueOrDefault() > 0)
                {
                    resource.LastConsumerSeenAtUtc = clock.UtcNow;
                    resource.LastUsedAtUtc = clock.UtcNow;
                }
                state.HasActiveManifestOwner = activeManifests.Any(manifest => manifest.OwnedResources.Contains(resource.ResourceId));
                var activeSchedules = resource.ManagementMode == SchedulingResourceManagementMode.DynamicScheduling
                    ? await scheduleStore.CountActiveByResourceAsync(resource.ResourceId, cancellationToken).ConfigureAwait(false)
                    : await scheduleStore.CountActiveByDestinationAsync(resource.Destination ?? resource.CanonicalName,
                        cancellationToken).ConfigureAwait(false);
                var isDynamic = resource.ManagementMode == SchedulingResourceManagementMode.DynamicScheduling;
                var decision = isDynamic
                    ? SchedulingVacuumEvaluator.Evaluate(resource, state, clock.UtcNow, settings, activeSchedules)
                    : ApplicationTopologyOrphanEvaluator.Evaluate(resource, state, clock.UtcNow,
                        options.Value.Vacuum.ApplicationTopology, activeSchedules);
                logger.LogInformation(
                    "Vacuum decision {Decision} for {ResourceId} transport {Transport}: {Detail}; mode={Mode}",
                    decision.Reason, resource.ResourceId, resource.Transport, decision.Detail,
                    isDynamic ? (settings.DryRun ? "DryRun" : "Automatic") : options.Value.Vacuum.ApplicationTopology.Mode);
                SchedulingMetrics.VacuumDecisions.Add(1,
                    new KeyValuePair<string, object?>("transport", resource.Transport),
                    new KeyValuePair<string, object?>("reason", decision.Reason.ToString()),
                    new KeyValuePair<string, object?>("resource.class", isDynamic ? "dynamic" : "application"));
                await registry.UpdateAsync(resource, cancellationToken).ConfigureAwait(false);
                var dryRun = isDynamic ? settings.DryRun : options.Value.Vacuum.ApplicationTopology.Mode == VacuumMode.DryRun;
                if (!decision.Eligible || dryRun) continue;
                if (!await leases.IsCurrentAsync(scope, _owner, fence.Value, clock.UtcNow, cancellationToken)
                        .ConfigureAwait(false))
                {
                    logger.LogWarning("Vacuum lease fencing rejected deletion of {ResourceId}", resource.ResourceId);
                    break;
                }
                if (!await manager.DeleteConditionallyAsync(resource, cancellationToken).ConfigureAwait(false))
                {
                    resource.LastError = "Conditional deletion was rejected because the resource became active.";
                    resource.Lifecycle = SchedulingResourceLifecycle.Active;
                    await registry.UpdateAsync(resource, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                resource.Lifecycle = SchedulingResourceLifecycle.Deleted;
                resource.DeletedAtUtc = clock.UtcNow;
                await registry.UpdateAsync(resource, cancellationToken).ConfigureAwait(false);
                deleted++;
            }
            return deleted;
        }
        finally
        {
            await leases.ReleaseAsync(scope, _owner, fence.Value, cancellationToken).ConfigureAwait(false);
        }
    }
}
