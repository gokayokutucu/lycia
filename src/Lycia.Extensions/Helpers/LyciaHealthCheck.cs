using Lycia.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lycia.Extensions.Helpers;

public sealed class LyciaHealthCheck(IServiceProvider serviceProvider) : IHealthCheck
{
    private const string Missing = "Missing";
    private const string Resolved = "Resolved";
    private const string Healthy = "Healthy";
    private const string Unhealthy = "Unhealthy";
    private const string Timeout = "Timeout";
    private const string Error = "Error";
    
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var details = new Dictionary<string, object>();

        // Small global timeout to avoid hanging health endpoint
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        var ct = cts.Token;

        // Saga Store
        var storeSvc = serviceProvider.GetService(typeof(ISagaStoreHealthCheck)) as ISagaStoreHealthCheck;
        var (storeOk, storeState) = await SafePingAsync(storeSvc, t => storeSvc!.PingAsync(t), ct);
        details["SagaStore"] = storeSvc is null ? Missing : storeState;

        // Event Bus
        var busSvc = serviceProvider.GetService(typeof(IEventBusHealthCheck)) as IEventBusHealthCheck;
        var (busOk, busState) = await SafePingAsync(busSvc, t => busSvc!.PingAsync(t), ct);
        if (busSvc is null)
        {
            // At least confirm it resolves via DI even if no health check impl
            details["EventBus"] = serviceProvider.GetService(typeof(IEventBus)) is not null ? Resolved : Missing;
            busOk = busSvc is not null; // if missing, count as not OK
        }
        else
        {
            details["EventBus"] = busState;
        }

        // Serializer (optional)
        var serializerSvc = serviceProvider.GetService(typeof(ISerializerHealthCheck)) as ISerializerHealthCheck;
        var (serializerOk, serializerState) = await SafePingAsync(serializerSvc, t => serializerSvc!.PingAsync(t), ct);
        if (serializerSvc is not null) details["Serializer"] = serializerState; else details["Serializer"] = Missing;

        // Outbox (optional)
        var outboxSvc = serviceProvider.GetService(typeof(IOutboxHealthCheck)) as IOutboxHealthCheck;
        var (outboxOk, outboxState) = await SafePingAsync(outboxSvc, t => outboxSvc!.PingAsync(t), ct);
        if (outboxSvc is not null) details["Outbox"] = outboxState; else details["Outbox"] = Missing;

        var okCount = (storeOk ? 1 : 0) + (busOk ? 1 : 0) + (serializerOk ? 1 : 0) + (outboxOk ? 1 : 0);
        var svcCount = (storeSvc is not null ? 1 : 0) + (busSvc is not null ? 1 : 0) + (serializerSvc is not null ? 1 : 0) + (outboxSvc is not null ? 1 : 0);

        if (svcCount == 0)
        {
            return HealthCheckResult.Unhealthy("Lycia infrastructure missing: no health-checked services registered", data: details);
        }

        // If we have registered checks but none are healthy → Unhealthy
        if (okCount == 0)
        {
            return HealthCheckResult.Unhealthy("Lycia infrastructure unhealthy: all registered components failing", data: details);
        }

        var allOk = storeOk && busOk && (serializerSvc is null || serializerOk) && (outboxSvc is null || outboxOk);
        return allOk 
            ? HealthCheckResult.Healthy("Lycia infrastructure healthy", details) 
            : HealthCheckResult.Degraded("Lycia infrastructure degraded", data: details); // Partial failure → Degraded
    }
    
    // Helper local function
    static async Task<(bool ok, string state)> SafePingAsync(object? svc, Func<CancellationToken, Task<bool>> ping, CancellationToken token)
    {
        if (svc is null) return (false, Missing);
        try
        {
            var ok = await ping(token).ConfigureAwait(false);
            return (ok, ok ? Healthy : Unhealthy);
        }
        catch (OperationCanceledException)
        {
            return (false, Timeout);
        }
        catch (Exception)
        {
            return (false, Error);
        }
    }
}
