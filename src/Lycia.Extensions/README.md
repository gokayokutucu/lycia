# Lycia.Extensions

Transport-independent building blocks for the Lycia Saga framework: fluent dependency-injection
registration (`AddLycia` with `ConfigureSaga`, `ConfigureEventBus`, `ConfigureRetry`,
`ConfigureLogging`), the middleware pipeline slots (logging, tracing, retry, custom middlewares),
Polly-based retry policies, the Newtonsoft JSON serializer, the transport-neutral outgoing
direct/Outbox pipeline, automatic persistence-topology resolution, and health checks.

## Registration

```csharp
services.AddLycia(configuration, lycia =>
{
    lycia.AddSagas().FromCurrentAssembly();
    lycia.UseTransport().RabbitMq();                 // Lycia.Extensions.RabbitMq (or .Nats() / .Kafka())
    lycia.UsePersistence().WithPostgreSqlSagaStore(  // a Lycia.Persistence.* package
        options => options.ConnectionString = connectionString);
});
```

The callback finalizes registration; there is no `.Build()` call. The older flat form
(`services.AddLycia(configuration).AddSagasFromCurrentAssembly().Build()` followed by
`services.AddLyciaRabbitMq()`) still compiles as an `[Obsolete]` wrapper.

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

RabbitMQ-specific code (event bus, listener, topology, TTL + DLX scheduling strategy) lives in
`Lycia.Extensions.RabbitMq`, and durable transport-independent scheduling in
`Lycia.Extensions.Scheduling`. Public namespaces were preserved, so code written against the earlier
combined package only needs the additional package references.
