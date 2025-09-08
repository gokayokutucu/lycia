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
- Hooks planned:
  - `IRetryPolicy` (Polly support pluggable)
  - `Lycia.Scheduling` module for delayed message processing

### Compensation Flow

- **Cycle Detection**: Guards against circular parent chains to prevent infinite compensation loops.
- Orchestration:
  - `CompensateAndBubbleUp` method for nested rollbacks
- Choreography:
  - Compensation triggered via `ISagaCompensationHandler<T>` interface
  - Handlers like `ReactiveSagaHandler<T>` and `StartReactiveSagaHandler<T>` used

### Logging & Observability


- **Status Tracking**: Every step has a `StepStatus`: `None`, `Started`, `Completed`, `Failed`, `Compensated`, `CompensationFailed`, `Cancelled`.  
  Transitions are strictly validated; invalid transitions (e.g., compensating an already-compensated step) are rejected.

- Step status logging: None, Started, Completed, Failed, Compensated, Cancelled
- Dead Letter Queue (RabbitMQ)
- Centralized correlation support: `SagaId`, `CorrelationId`

---

## ⚙️ Fluent Configuration

```csharp
services.AddLycia(configuration)
        .UseMessageSerializer<CustomSerializer>()
        .UseEventBus<KafkaEventBus>()
        .UseSagaStore<MongoSagaStore>()
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

## 🗃 InMemorySagaStore

A lightweight, non-persistent store ideal for unit tests or in-memory dev scenarios.

- **Keying**: Uses a composite key of `SagaId`, `MessageId`, and `HandlerType`. All step metadata is stored in memory dictionaries.
- **Idempotency**: Prevents the same message/step from being reprocessed by enforcing uniqueness on keys.


## 🧪 Integration Tests

- `RabbitMqEventBusIntegrationTests` – verifies serialization headers
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

- Add support for Avro / Protobuf with Schema Registry
- Finalize `IRetryPolicy` and `Lycia.Scheduling` module
- Improve distributed tracing and observability
- Add Outbox/Inbox pattern with persistence layer

---

For questions or contributions, feel free to open an issue or start a discussion on the project repo.

---

## ✍️ Naming Conventions

Lycia enforces a consistent naming convention across store and bus implementations to enhance clarity and maintainability:

- **RedisSagaStore**
  - Keys use the format: `lycia:saga:{ApplicationId}:{SagaId}:{HandlerType}`
  - Compensation chains are traceable via `ParentMessageId` embedded in context headers
  - All keys are prefixed with `lycia:` to ensure namespace isolation

- **RabbitMqEventBus**
  - Routing keys use the pattern: `{applicationId}.{messageType}`
  - Headers include standardized fields such as `lycia-type`, `lycia-schema-id`, `lycia-schema-ver`
  - All events carry `CorrelationId`, `SagaId`, and `MessageId` to support distributed tracing

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

Lycia supports three distinct Saga patterns, each serving a different coordination style:

### 1. **Choreography (Reactive Saga)**
- Event-driven
- Only failure events are published (`SomethingFailedEvent`)
- Compensation handlers react to these failure events
- No central coordinator
- Stateless

### 2. **Sequential Orchestration**
- Coordinated flow with ordered steps
- When a step fails, `Context.MarkAsFailed<T>()` triggers compensation via `CompensateAndBubbleUp()`
- Compensation walks backward through the chain using `ParentMessageId`

### 3. **Classic Orchestration**
- Centralized handler using `StartCoordinatedSagaHandler<T>` or `CoordinatedSagaHandler<T>`
- All transitions, step results, and compensations are controlled by the orchestrator
