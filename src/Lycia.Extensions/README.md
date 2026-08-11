# Lycia.Extensions

Transport-independent building blocks for the Lycia Saga framework: fluent dependency-injection
registration (`AddLycia` with `ConfigureSaga`, `ConfigureEventBus`, `ConfigureRetry`,
`ConfigureLogging`), the middleware pipeline slots (logging, tracing, retry, custom middlewares),
Polly-based retry policies, the Newtonsoft JSON serializer, the transport-neutral outgoing
direct/Outbox pipeline, and health checks.

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

`AddLycia` binds options, registers core saga services, middleware, serializer, the Redis saga
store and health checks. It no longer registers a transport: resolve `IEventBus` without a
transport package and you get a clear error naming the packages to reference.

## Package split

RabbitMQ-specific code (event bus, listener, topology, TTL + DLX scheduling strategy) moved to
`Lycia.Extensions.RabbitMq`. Durable transport-independent scheduling (SchedulerWorker, Redis
schedule store, vacuum workers, `AddLyciaScheduling`) moved to `Lycia.Extensions.Scheduling`.
Public namespaces were preserved, so migrating is adding the package reference(s) and calling
`services.AddLyciaRabbitMq()` where RabbitMQ was previously implicit.
