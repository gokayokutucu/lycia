// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Scheduling;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Lycia.Extensions.Scheduling;

/// <summary>Checks scheduling-store connectivity and reports the configured dispatch strategy.</summary>
public sealed class SchedulingHealthCheck(IScheduleStore store, IOptions<SchedulingOptions> options) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
            return HealthCheckResult.Healthy("Lycia scheduling is disabled by configuration.");
        try
        {
            _ = await store.CountActiveByResourceAsync("__lycia_health_check__", cancellationToken)
                .ConfigureAwait(false);
            return HealthCheckResult.Healthy("Lycia scheduling store is reachable.", new Dictionary<string, object>
            {
                ["WorkerEnabled"] = options.Value.Worker.Enabled,
                ["PreferNativeTransportScheduling"] = options.Value.PreferNativeTransportScheduling,
                ["DynamicDelaysEnabled"] = options.Value.AllowDynamicDelays
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Lycia scheduling store is unavailable.", exception);
        }
    }
}
