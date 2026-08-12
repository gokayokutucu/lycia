# Lycia.Extensions.Scheduling

Transport-independent durable scheduling for the Lycia Saga framework: `SchedulerWorker`
orchestration, Redis-backed and in-memory schedule stores, schedule claiming with expiring leases,
renewal and monotonic fencing tokens, idempotent state transitions, the scheduling resource
registry, topology manifest and vacuum workers, plus scheduling metrics, tracing, health checks
and validated configuration.

## Registration

```csharp
// Redis-backed durable scheduling (requires an IConnectionMultiplexer registration)
services.AddLyciaScheduling(options =>
{
    options.AllowDynamicDelays = false;
    options.Worker.LeaseDuration = TimeSpan.FromSeconds(30);
    options.Worker.LeaseRenewInterval = TimeSpan.FromSeconds(10);
    options.Vacuum.ApplicationTopology.Mode = VacuumMode.ReportOnly;
});

// Deterministic in-memory scheduling for tests and single-process development
services.AddLyciaInMemoryScheduling();
```

The nested `AddLycia(...)` DSL form is equivalent and preferred for new code:

```csharp
lycia
    .AddScheduling()
        .WithRedisStore()
        .WithPredefinedDelays()
        .WithDispatch(options =>
        {
            options.LeaseDuration = TimeSpan.FromSeconds(30);
            options.LeaseRenewInterval = TimeSpan.FromSeconds(10);
        })
        .WithVacuum(options => options.ApplicationTopology.Mode = VacuumMode.ReportOnly);
```

`WithDispatch(...)` configures the same `SchedulingOptions.Worker` settings as `options.Worker` above
— batching, claim lifetime, lease renewal, and bounded backoff-with-jitter retry for due-schedule
dispatch. It replaces `WithWorker(...)`, kept as an `[Obsolete]` wrapper for existing callers.

Schedule from any saga context:

```csharp
var scheduleId = await Context.Schedule(
    new CancelOrderCommand { OrderId = orderId },
    ScheduleDelay.ThirtySeconds,
    cancellationToken);
```

## Design

Scheduling contracts (`IScheduleStore`, `IMessageScheduler`, `ScheduleDelay`, vacuum abstractions)
live in `Lycia.Saga.Abstractions` and the saga `Context.Schedule` APIs remain in the core packages.
This package hosts the durable workers and stores. It never depends on a transport: transports that
support native delays (for example RabbitMQ's TTL + DLX strategy in `Lycia.Extensions.RabbitMq`)
plug in through the `INativeSchedulingTransport` contract, and everything else uses the durable
`SchedulerWorker` path.

Redis creation and claiming use scripts. A claim has an expiring lease and monotonic fencing token;
active dispatches renew the lease, and every state mutation rejects stale owners. Completion occurs
after final transport acceptance, so dispatch is at least once and handlers must stay idempotent.
Vacuum ownership comes from the durable registry, never a name prefix, and ordinary application
topology defaults to `ReportOnly` with a destructive double opt-in. The `Lycia.Scheduling` activity
source and meter plus the `LyciaScheduling` health check avoid payload data.

## Migrating from Lycia / Lycia.Extensions

Before the package split, the scheduling workers shipped inside `Lycia` and the Redis scheduling
store inside `Lycia.Extensions`. Now add a package reference to `Lycia.Extensions.Scheduling`.
Namespaces are unchanged (`Lycia.Scheduling`, `Lycia.Extensions.Scheduling`), so existing `using`
directives, options sections, and `AddLyciaScheduling` / `AddLyciaInMemoryScheduling` calls keep
compiling.
