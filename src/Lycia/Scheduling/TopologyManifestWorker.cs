// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lycia.Scheduling;

/// <summary>Registers replica heartbeats while sharing canonical topology ownership across one logical application.</summary>
public sealed class TopologyManifestWorker(
    ITopologyManifestRegistry registry,
    ISchedulingResourceRegistry resources,
    IEventBus eventBus,
    IDictionary<string, (Type MessageType, Type HandlerType)> topology,
    ISchedulingClock clock,
    IOptions<SchedulingOptions> options,
    ILogger<TopologyManifestWorker> logger) : BackgroundService
{
    private readonly DateTimeOffset _startedAtUtc = clock.UtcNow;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly string _deploymentId = Environment.GetEnvironmentVariable("LYCIA_DEPLOYMENT_ID") ?? "local";

    /// <summary>Writes one deterministic heartbeat and returns the manifest for health probes and tests.</summary>
    public async Task<TopologyManifest> HeartbeatOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var transport = eventBus is ISchedulingResourceManager manager
            ? manager.TransportName
            : eventBus.GetType().Name.Replace("EventBus", string.Empty).ToLowerInvariant();
        var canonicalApplicationKey = EndpointIdentityNormalizer.Default.Normalize(eventBus.ApplicationId);
        foreach (var pair in topology)
        {
            await resources.UpsertAsync(new SchedulingResourceRecord
            {
                ResourceId = pair.Key,
                Transport = transport,
                ResourceType = "application-topology",
                CanonicalName = pair.Key,
                CanonicalApplicationKey = canonicalApplicationKey,
                MessageType = pair.Value.MessageType.AssemblyQualifiedName,
                MessageKind = ToScheduledKind(MessageKindResolver.Resolve(pair.Value.MessageType)),
                Destination = pair.Key,
                ManagementMode = SchedulingResourceManagementMode.LyciaManaged,
                Lifecycle = SchedulingResourceLifecycle.Active,
                CreatedAtUtc = _startedAtUtc,
                LastDeclaredAtUtc = now,
                LastUsedAtUtc = now,
                FrameworkVersion = typeof(TopologyManifestWorker).Assembly.GetName().Version?.ToString() ?? "unknown",
                TopologyVersion = "1"
            }, cancellationToken).ConfigureAwait(false);
        }
        var manifest = new TopologyManifest
        {
            ApplicationId = eventBus.ApplicationId,
            CanonicalApplicationKey = canonicalApplicationKey,
            DeploymentId = _deploymentId,
            InstanceId = _instanceId,
            StartedAtUtc = _startedAtUtc,
            LastHeartbeatAtUtc = now,
            OwnedResources = new HashSet<string>(topology.Keys, StringComparer.Ordinal)
        };
        await registry.HeartbeatAsync(manifest, cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    private static ScheduledMessageKind ToScheduledKind(MessageKind kind) => kind switch
    {
        MessageKind.Command => ScheduledMessageKind.Command,
        MessageKind.Event => ScheduledMessageKind.Event,
        MessageKind.Response => ScheduledMessageKind.Response,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported topology message kind.")
    };

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await HeartbeatOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Lycia topology manifest heartbeat failed"); }
            await Task.Delay(options.Value.ManifestHeartbeatInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
