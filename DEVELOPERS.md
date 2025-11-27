# Lycia Developer Documentation

This document provides an in-depth look into the architecture, components, configuration, and internals of the Lycia Saga Infrastructure. It complements the public-facing `README.md` with implementation details, design decisions, and extensibility points.

---

## 🚀 Architecture Overview

### Core Components

- **SagaDispatcher**
  - Routes incoming messages to the appropriate handler
  - Performs idempotency checks
  - Catches exceptions and routes them through the error flow

- **SagaCompensationCoordinator**
  - Executes compensation chains in reverse
  - Performs cycle detection and valid state transitions
  - Used for orchestrated and reactive saga compensation

- **SagaContext**
  - Manages step state and message-specific state.
  - Includes methods like `MarkAsComplete<T>()`, `MarkAsFailed<T>()`, `MarkAsCancelled<T>()`

---

## 🧩 Key Features

### Idempotency

- `Context.IsAlreadyCompleted<T>()` helps guard against duplicate handling
- Global default: `SagaOptions.DefaultIdempotency`
- Per-handler override: `protected bool EnforceIdempotency`

### Cancellation / Timeout / Retry

- CancellationToken flows through all handlers
- `Context.MarkAsCancelled<T>()` updates status
- Hooks finalized:
  - `IRetryPolicy` (with Polly-based default implementation, configurable via ConfigureRetry)
  - Supports exponential backoff, jitter, and per-exception retry strategies
  - `Lycia.Scheduling` module for delayed message processing and extended scheduling for delayed retries

### Compensation Flow

- **Cycle Detection**: Guards against circular parent chains to prevent infinite compensation loops.
- Orchestration:
  - `CompensateAndBubbleUp` method for nested rollbacks
- Choreography:
  - Compensation triggered via `ISagaCompensationHandler<T>` interface
  - Handlers like `ReactiveSagaHandler<T>` and `StartReactiveSagaHandler<T>` used

### Logging & Observability

- **ILogger Integration**: Replaces previous Console.WriteLine usage for structured logging
- **ISagaContextAccessor**: Provides ambient saga context access in async flows
- **Status Tracking**: Every step has a `StepStatus`: `None`, `Started`, `Completed`, `Failed`, `Compensated`, `CompensationFailed`, `Cancelled`.  
  Transitions are strictly validated; invalid transitions (e.g., compensating an already-compensated step) are rejected.

- Step status logging: None, Started, Completed, Failed, Compensated, Cancelled
- Dead Letter Queue (RabbitMQ)
- Centralized correlation support: `SagaId`, `CorrelationId`

---

## OpenTelemetry Tracing (Optional)

Lycia provides native hooks for distributed tracing via **ActivitySource** and OpenTelemetry.
Tracing is optional and resides in the `Lycia.Extensions.OpenTelemetry` package.

### Enabling Tracing

Install the following packages:

```
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package Lycia.Extensions.OpenTelemetry
```

Then configure:

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

---

## 🗃 InMemorySagaStore

A lightweight, non-persistent store ideal for unit tests or in-memory dev scenarios.

- **Keying**: Uses a composite key of `SagaId`, `MessageId`, and `HandlerType`. All step metadata is stored in memory dictionaries.
- **Idempotency**: Prevents the same message/step from being reprocessed by enforcing uniqueness on keys.


## 🧪 Integration Tests

- `RabbitMqEventBusIntegrationTests` – verifies serialization headers and Ack/Nack/DLQ behavior
- `RedisSagaStoreIntegrationTests` – includes cancellation and TTL testing
- `RabbitMqSagaCompensationIntegrationTests` – full compensation logic
- `FakeSagaContext` test doubles used instead of heavy mocks

---

## 🗂 Appsettings Example

```json
{
  "ApplicationId": "SampleOrderApi",
  "Lycia": {
    "EventBus": {
      "Provider": "RabbitMQ",
      "ConnectionString": "amqp://guest:guest@127.0.0.1:5672/"
    },
    "EventStore": {
      "Provider": "Redis",
      "ConnectionString": "127.0.0.1:6379",
      "LogMaxRetryCount": 5
    },
    "Saga": {
      "DefaultIdempotency": true
    },
    "CommonTTL": 3600
  }
}
```

---

## 🔮 Roadmap

- Add Outbox/Inbox pattern with persistence layer
- Add support for Avro / Protobuf with Schema Registry (including the built‑in `AvroSchemaConverter`)
- Finalize `IRetryPolicy` (done) and extend `Lycia.Scheduling` module for delayed retries
- Improve distributed tracing and observability

---

For questions or contributions, feel free to open an issue or start a discussion on the project repo.

---

## ✍️ Naming Conventions

Lycia enforces a consistent naming convention across store and bus implementations to enhance clarity and maintainability. Use a unique `ApplicationId` per service/consumer and always bind queues with concrete handler types to avoid cross-service collisions.

- **MessagingNamingHelper (RabbitMQ bindings)**
  - Consumer queue/routing key format: `{event|command|response}.{MessageType}.{HandlerType}.{ApplicationId}` (e.g., `event.OrderCreatedEvent.CreateOrderSagaHandler.OrderService`)
  - Publisher topic pattern: `{event|command|response}.{MessageType}.#` – used only when publishing, never for queue declarations
  - Exchange name: `{event|command|response}.{MessageType}`; shared between publishers and consumers
  - Keep handler types non-generic and `ApplicationId` unique per service to prevent queues from overlapping

- **RedisSagaStore / InMemorySagaStore**
  - Step metadata keys use `step:{StepName}:handler:{HandlerName}:message-id:{MessageId}`
  - Compensation chains remain traceable via `ParentMessageId` embedded in context headers
  - Keys include `message-id` to enforce idempotency across retries

- **RabbitMqEventBus**
  - Headers include standardized fields such as `lycia-type`, `lycia-schema-id`, `lycia-schema-ver`
  - All events carry `CorrelationId`, `SagaId`, and `MessageId` to support distributed tracing

- **Middleware Slots**
  - Middleware interfaces such as `ILoggingSagaMiddleware` and `IRetrySagaMiddleware` exist for logging and retry logic
  - These middleware components are replaceable to customize the pipeline

---

## 🧾 Message vs Correlation IDs

Understanding `MessageId` and `CorrelationId` is essential for building traceable and idempotent saga workflows in Lycia.

- **MessageId**
  - A unique identifier for the individual message instance
  - May remain the same or change across publish/retry/replay depending on the transport
  - Used for deduplication, logging, tracing, and replay logic
  - Answers: *“Is this message uniquely identifiable?”*

- **CorrelationId**
  - Used to group multiple messages as part of a single saga or business workflow
  - For example, `OrderCreated` → `OrderShipped` → `OrderDelivered` may share the same `CorrelationId`
  - Used for tracing, distributed logging, and Saga correlation
  - Answers: *“Which workflow or saga does this message belong to?”*

---

## 🧬 Saga Types in Lycia

Lycia supports **three primary Saga coordination patterns**, each designed for different messaging and workflow requirements.  
The key distinctions are **stateful vs. stateless**, **centralized vs. decentralized**, and **request–response vs. event-driven**.

---

### 1. **Choreography (Reactive Saga)**
- Pure event‑driven flow
- No central coordinator
- Stateless (no `TSagaData`)
- Each handler reacts to an event independently
- Compensation triggered via `ISagaCompensationHandler<T>`
- Implemented with:
  - `StartReactiveSagaHandler<TStart>`
  - `ReactiveSagaHandler<TMessage>`
  - `ISagaCompensationHandler<TMessage>`

---

### 2. **Sequential Orchestration (Coordinated Saga)**
- Centralized orchestration logic
- Stateful (`TSagaData` required)
- Steps progress in an ordered sequence
- Failures trigger compensation via `CompensateAndBubbleUp()`
- Ideal for multi‑step business workflows
- Implemented with:
  - `StartCoordinatedSagaHandler<TStart, TSagaData>`
  - `CoordinatedSagaHandler<TMessage, TSagaData>`

---

### 3. **Classic Orchestration (Coordinated + Request–Response)**
- Central coordinator with **asynchronous request–response** flow
- Stateful (`TSagaData`)
- Each step sends a command and waits for a corresponding response
- Includes full success/fail handlers per response type
- Ideal for workflows where each action has a definitive result
- Implemented with:
  - `StartCoordinatedResponsiveSagaHandler<TStart, TResponse, TSagaData>`
  - `CoordinatedResponsiveSagaHandler<TStart, TResponse, TSagaData>`
  - `IResponseSagaHandler<TResponse>`

**This is the pattern used in `Sample.Order.Orchestration.Consumer`.**

---


## ⚙️ Fluent Configuration

### Tracing Integration

Lycia optionally supports OpenTelemetry via the `Lycia.Extensions.OpenTelemetry` package.

```csharp
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "order-orchestration-consumer",
            serviceVersion: "1.0.0"
        ))
    .AddLyciaTracing()
    .WithTracing(tp =>
    {
        tp.AddSource("Lycia");
        tp.AddAspNetCoreInstrumentation();
        tp.AddOtlpExporter(options => options.Endpoint = new Uri("http://localhost:4317"));
    });
```

```csharp
services.AddLycia(configuration)
        .UseMessageSerializer<CustomSerializer>()
        .UseEventBus<RabbitMqEventBus>()
        .UseSagaStore<RedisSagaStore>()
        .AddSagasFromAssemblies(typeof(SomeHandler).Assembly)
        .Build();
```

- Builder APIs:
  - `UseMessageSerializer<T>()`, `UseEventBus<T>()`, `UseSagaStore<T>()`
  - `AddSagasFromCurrentAssembly()`, `AddSagasFromAssemblies(...)`
  - `ConfigureSaga(...)`, etc.

### Queue Type Map

- `_LyciaHandlerDiscovery` resolves types:
  - `SafeGetTypes`, `IsSagaHandlerBase`, `ImplementsAnySagaInterface`
  - `GetMessageTypesFromHandler()` analyzes interface and base classes

---


---
