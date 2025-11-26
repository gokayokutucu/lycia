# Lycia

[![NuGet](https://img.shields.io/nuget/v/Lycia.svg)](https://www.nuget.org/packages/Lycia)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Lycia.svg)](https://www.nuget.org/packages/Lycia)
![Target Framework](https://img.shields.io/badge/.NET-netstandard2.0%20%7C%20net8.0%20%7C%20net9.0-blue)
[![Build](https://github.com/gokayokutucu/lycia/actions/workflows/dotnet.yml/badge.svg)](https://github.com/gokayokutucu/lycia/actions/workflows/dotnet.yml)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![GitHub release](https://img.shields.io/github/v/release/gokayokutucu/lycia)](https://github.com/gokayokutucu/lycia/releases)

**Lycia** is the **main package** of the Lycia framework.  
It provides the saga infrastructure, orchestration, and choreography support.  
Extensions (e.g. Redis, RabbitMQ, Scheduling, Observability) are published separately under `Lycia.Extensions.*`.

**Lycia** began with a vision on *May 28, 2023*.  
Our motto: *“Turning difficult paths into joyful simplicity.”* Inspired by the ancient *Lycian Way*, we set out to build a framework that makes complex saga workflows easy to manage — supported by strong documentation and aligned with modern software practices.

Lycia is a messaging framework (Message-oriented Middleware, MoM) built for .NET applications, supporting .NET Standard 2.0 and higher. It provides a robust foundation for distributed systems where reliable message flow and state coordination are essential.

For architectural deep-dive, compensation coordination, and integration test strategies, see [DEVELOPERS.md](DEVELOPERS.md).

---

## Getting Started / Samples

Explore the [samples/](samples) folder for real-world usage:  
- **Sample.Order.Api** – API entrypoint  
- **Sample.Order.Orchestration.Consumer** – Coordinated Responsive Saga (asynchronous request–response orchestration using `CoordinatedResponsiveSagaHandler`)  
- **Sample.Order.Choreography.Consumer** – Reactive Saga (stateless event-driven choreography using `ReactiveSagaHandler`)  
- **Sample.Order.Orchestration.Seq.Consumer** – Coordinated Saga (stateful sequential orchestration using `CoordinatedSagaHandler`, with compensation flows)  

---

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

- Built-in support for **idempotency**, **timeouts**, and **in-process retries with Polly, Ack/Nack + DLQ support on RabbitMQ**  
- **Default Middleware Pipeline (Logging + Tracing + Retry, replaceable via UseSagaMiddleware)**  
- **Extensibility**: Easily plug in custom implementations of `IMessageSerializer`, `IEventBus`, or `ISagaStore`.

---

## Quick Start

**Coordinated Saga**: Uses a central orchestrator to manage the full lifecycle of a saga. This handler starts a saga, executes the business logic step-by-step, and coordinates the flow by publishing commands/events. Ideal when you need deterministic, centralized control.

**Stateful Model**: Coordinated sagas always use `TSagaData` to maintain saga state across all steps.

**Coordinated Saga (Orchestration)**

```csharp
public class CreateInvoiceSagaHandler :
    StartCoordinatedSagaHandler<CreateInvoiceCommand, CreateInvoiceSagaData>
{
    public override async Task HandleAsync(CreateInvoiceCommand cmd, CancellationToken ct = default)
    {
        // business logic
        await Context.Publish(new InvoiceStartedEvent { InvoiceId = cmd.InvoiceId }, ct);
        await Context.MarkAsComplete<CreateInvoiceCommand>(ct);
    }
}
```

**Reactive Saga**: Implements event-driven choreography. Each handler reacts only to the event it subscribes to. There is no central orchestrator; instead, services collaborate by emitting events. Ideal for loosely coupled systems and autonomous microservices.

**Stateless Model**: Reactive sagas do not use saga state (`TSagaData`); each event is handled independently without maintaining cross-step state.

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

### Additional Saga Handler Examples

**Coordinated Responsive Saga**: Similar to a coordinated saga, but also handles direct responses (e.g., request/response patterns). Useful when the saga step must wait for a specific success or failure message before moving forward.

Like all coordinated sagas, this pattern is **stateful** and requires a `TSagaData` object to track progress across asynchronous request–response steps.

**CoordinatedResponsiveSagaHandler**

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
    
    public override async Task HandleSuccessResponseAsync(OrderCreatedResponse response, CancellationToken cancellationToken = default)
    {
        // Order created, reserve inventory
        await Context.Send(new ReserveInventoryCommand
        {
            OrderId = response.OrderId,
            ParentMessageId = response.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<OrderCreatedResponse>();
    }
}
```

---

## OpenTelemetry Tracing (Optional)

Lycia provides native hooks for distributed tracing via **ActivitySource** and OpenTelemetry.
Tracing is intentionally kept optional and resides in the `Lycia.Extensions.OpenTelemetry` package.

### Enabling Tracing

Install the following NuGet packages in your host application:

```
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package Lycia.Extensions.OpenTelemetry
```

Then configure tracing:

```csharp
builder.Services.AddOpenTelemetry()
    .AddLyciaTracing() // adds Lycia ActivitySource + propagation
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation();
        t.AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri("http://otel-collector:4317");
        });
    });
```

### What Lycia Emits

- A span per saga step (`Saga.<HandlerName>`)
- Attributes:
  - `lycia.saga.id`
  - `lycia.message.id`
  - `lycia.correlation.id`
  - `lycia.application.id`
  - `lycia.saga.step.status`
- Automatic W3C trace propagation through messages (RabbitMQ / EventBus)

### How It Works

Tracing is added without requiring any saga code changes:
- The middleware creates spans around each handler invocation.
- `LyciaTracePropagation` injects `traceparent`/`tracestate` into message headers.
- The listener extracts headers and restores parent-child relationships.

This produces a full cross-service trace chain in Grafana Tempo or Jaeger.

---

## Timeline

- **May 28, 2023** – The idea was born.  
- **Initial Goal** – To provide a saga framework that avoids complexity and is easy to use by anyone.  
- **Today** – Development accelerated by "vibe-coding"; includes tests, integrations, and real-world usage scenarios.

---

## What's Next

- **Native Inbox / Outbox Guarantees**
  - State-consistency
  - Cross-service delivery reliability
  - Message replay safety
  
- **Distributed Delayed Message Scheduling**
  - Compensation timers
  - Cron-like orchestration intervals
  - Durable timing guarantees

- **Schema Intelligence**
  - Avro/Protobuf registry integration (including the built‑in `AvroSchemaConverter`)
  - Backward/forward compatibility detection
  - Contract-driven saga evolution

## License

This project is licensed under the [Apache 2.0 License](LICENSE).
---

## Why Lycia? (Deep Dive Highlights)

In addition to minimal setup and clear semantics, Lycia offers:

- **SagaDispatcher** and **CompensationCoordinator** core components  
- **Built-in Idempotency** and cancellation flow (`MarkAsCancelled<T>()`)  
- **Custom Retry Hooks** finalized via `IRetryPolicy` (with Polly-based default implementation, configurable via `ConfigureRetry`), supporting exponential backoff, jitter, and per-exception retry strategies  
- **Choreography & Orchestration** support via `ReactiveSagaHandler<T>` and `CoordinatedSagaHandler<T>`  
- **RedisSagaStore** built-in extension support with TTL, CAS, parent-child message tracing  
- **RabbitMQ EventBus** built-in extension support with Dead Letter Queue (DLQ) and header normalization  
- **ISagaContextAccessor** for contextual saga state access  
- **Fluent Middleware Pipeline**: Default logging, retry, and tracing middleware (via `ActivityTracingMiddleware`), all replaceable via middleware slots (`ILoggingSagaMiddleware`, `IRetrySagaMiddleware`, `ITracingSagaMiddleware`)  
- **Fluent Configuration API**: Easily plug your custom serializers, stores and buses  
- **Detailed Integration Tests** for Redis, RabbitMQ (including Ack/Nack/DLQ behavior), Compensation logic  
- **Appsettings.json Support**: Environment-based saga configuration
