# Lycia.Extensions

Transport-independent building blocks for the Lycia Saga framework: fluent dependency-injection
registration (`AddLycia` with `ConfigureSaga`, `ConfigureEventBus`, `ConfigureRetry`,
`ConfigureLogging`), the middleware pipeline slots (logging, tracing, retry, custom middlewares),
Polly-based retry policies, the Newtonsoft JSON serializer, the transport-neutral outgoing
direct/Outbox pipeline, automatic persistence-topology resolution, and health checks.

## Registration

```csharp
services.AddLycia(configuration)
        .AddSagasFromCurrentAssembly()
        .Build();

// Then register a transport package:
services.AddLyciaRabbitMq();            // Lycia.Extensions.RabbitMq
// services.AddLyciaNats(o => ...);     // Lycia.Extensions.Nats
// services.AddLyciaKafka(o => ...);    // Lycia.Extensions.Kafka
```

`AddLycia` binds options and registers core saga services, middleware, serializer, and health checks.
Concrete persistence and transport selection remains explicit. Resolve `IEventBus` without a
transport package and you get a clear error naming the packages to reference.

`UsePersistence()` defaults to automatic boundary selection. Compatible SQL Server/PostgreSQL
stores in one database resolve `LocalAtomic`; mixed providers resolve `Independent`. Use
`RequireAtomicBoundary()` as a startup assertion or `UseIndependentTransactions()` as an explicit
opt-out. This is a service-local Lycia boundary, not distributed or exactly-once processing.

`AddLycia` also registers `ILyciaReliabilityDiagnostics`, a safe, secret-free snapshot of the active
persistence topology (provider names, resolved transaction boundary, which of Inbox/Outbox/journal/
rebuild are enabled) for diagnostics and startup logging.

## Package split

RabbitMQ-specific code (event bus, listener, topology, TTL + DLX scheduling strategy) moved to
`Lycia.Extensions.RabbitMq`. Durable transport-independent scheduling (SchedulerWorker, Redis
schedule store, vacuum workers, `AddLyciaScheduling`) moved to `Lycia.Extensions.Scheduling`.
Public namespaces were preserved, so migrating is adding the package reference(s) and calling
`services.AddLyciaRabbitMq()` where RabbitMQ was previously implicit.
