# Lycia

**Lycia** began with a vision on *May 28, 2023*.
Our motto: *“Turning difficult paths into joyful simplicity.”* Inspired by the ancient *Lycian Way*, we set out to build a framework that makes complex saga workflows easy to manage — supported by strong documentation and aligned with modern software practices.

Lycia is a messaging framework (Message-oriented Middleware, MoM) built for .NET applications, supporting .NET Standard 2.0 and higher. It provides a robust foundation for distributed systems where reliable message flow and state coordination are essential.

For architectural deep-dive, compensation coordination, and integration test strategies, see [DEVELOPERS.md](DEVELOPERS.md).

## Our Mission

- **Simplicity**: Define complex orchestration flows with ease.  
- **Flexibility**: Support both orchestration (which we call *Coordinated Saga*) and choreography (our term: *Reactive Saga*) patterns.  
- **Portability**: Work out of the box with popular infrastructures like RabbitMQ and Redis.  
- **Robust Documentation**: Step-by-step guides, code samples, and best practices to lead the way.

---

## What Makes Lycia Different

Unlike other frameworks, Lycia offers:

- **Minimal Setup** – Start with a single line:

```csharp
services.AddLycia(Configuration)
        .AddSagasFromCurrentAssembly()
        .Build();
```

- **Clear Naming and Semantics**:  
  - *Coordinated Saga* → central orchestrator-based saga management  
  - *Reactive Saga* → event-driven choreography approach  

- Built-in support for **idempotency**, **timeouts**, and **retries**  
- **Extensibility**: Easily plug in custom implementations of `IMessageSerializer`, `IEventBus`, or `ISagaStore`.

---

## Quick Start

**Coordinated Saga (Orchestration)**

```csharp
public class CreateOrderSagaHandler :
    StartCoordinatedResponsiveSagaHandler<CreateOrderCommand, OrderCreatedResponse, CreateOrderSagaData>
{
    public override async Task HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        // Business logic
        await Context.Publish(new OrderCreatedResponse { OrderId = cmd.OrderId }, ct);
        await Context.MarkAsComplete<CreateOrderCommand>(ct);
    }
}
```

**Reactive Saga (Choreography)**

```csharp
public class InventorySagaHandler :
    ReactiveSagaHandler<OrderCreatedEvent>,
    ISagaCompensationHandler<PaymentFailedEvent>
{
    public override async Task HandleAsync(OrderCreatedEvent evt, CancellationToken ct = default)
    {
        // Reserve inventory
        await Context.Publish(new InventoryReservedEvent { OrderId = evt.OrderId }, ct);
        await Context.MarkAsComplete<OrderCreatedEvent>(ct);
    }

    public Task CompensateAsync(PaymentFailedEvent failed, CancellationToken ct = default)
    {
        // Release reserved stock
        InventoryService.ReleaseStock(failed.OrderId);
        return Task.CompletedTask;
    }
}
```

---

## Timeline

- **May 28, 2023** – The idea was born.  
- **Initial Goal** – To provide a saga framework that avoids complexity and is easy to use by anyone.  
- **Today** – Development accelerated by "vibe-coding"; includes tests, integrations, and real-world usage scenarios.

---

## What's Next

- Kafka support  
- Schema registry (Avro/Protobuf) integration  
- Advanced observability (metrics, tracing, logging)  
- Native support for Outbox/Inbox pattern

## License

This project is licensed under the [Apache 2.0 License](LICENSE).
---

## Why Lycia? (Deep Dive Highlights)

In addition to minimal setup and clear semantics, Lycia offers:

- **SagaDispatcher** and **CompensationCoordinator** core components
- **Built-in Idempotency** and cancellation flow (`MarkAsCancelled<T>()`)
- **Custom Retry Hooks** planned via `IRetryPolicy` abstraction
- **Choreography & Orchestration** support via `ReactiveSagaHandler<T>` and `StartCoordinatedSagaHandler<T>`
- **RedisSagaStore** built-in extension support with TTL, CAS, parent-child message tracing
- **RabbitMQ EventBus** built-in extension support with Dead Letter Queue (DLQ) and header normalization
- **Fluent Configuration API**: Easily plug your custom serializers, stores and buses
- **Detailed Integration Tests** for Redis, RabbitMQ, Compensation logic
- **Appsettings.json Support**: Environment-based saga configuration
