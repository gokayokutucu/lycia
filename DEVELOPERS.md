# Lycia Developer Documentation

This document provides an in-depth look into the architecture, components, configuration, and internals of the Lycia Saga Infrastructure. It complements the public-facing `README.md` with implementation details, design decisions, and extensibility points.

---

## Transport-independent scheduling

Scheduling contracts live in `Lycia.Saga.Abstractions`, orchestration and deterministic in-memory components live in
`Lycia`, and Redis/RabbitMQ implementations live in `Lycia.Extensions`. Kafka and the supported NATS baseline use the
same durable-worker path, keeping transport-specific behavior out of the core scheduler.

Redis creation and claiming use scripts. A claim has an expiring lease and monotonic fencing token; active dispatches
renew the lease, and every state mutation rejects stale owners. The stored payload retains its original `MessageId`.
Responses also retain the request payload so dispatch invokes `Respond(request, response)` and preserves targeted
`ResponseEndpoint`, `RequestId`, correlation, causation, parent, and saga identity. Completion occurs after final
transport acceptance. A crash between acceptance and completion can repeat a message, so the guarantee is at least
once rather than exactly once.

RabbitMQ predefined scheduling is fixed queue TTL plus DLX. Dynamic arbitrary-delay queues are opt-in and registered
with exact provenance. Vacuum uses a per-transport distributed lease/fence, active replica manifests, schedule
references, broker message/consumer facts, age/idle thresholds, and conditional deletion. Ordinary topology follows a
separate orphan/quarantine state machine and defaults to report-only; inactivity or a matching name never proves
ownership. The `Lycia.Scheduling` activity source/meter and `LyciaScheduling` health check avoid payload data.

---

## Request-response implementation contract

Use `Context.Send` for owned commands, `Context.Respond` for targeted replies, and `Context.Publish` only
for facts. The context centralizes identity propagation. Commands get a fresh `MessageId`, self
`RequestId`, and current correlation, saga, causation, parent and response endpoint. Responses get a
fresh identity, the request `MessageId` as request, causation and parent, and the waiting application's
canonical endpoint. Events get workflow and lineage metadata but no request metadata. Redelivery
preserves all identities.

`ParentMessageId` alone drives compensation and bubble-up; `RequestId` matches a pair and `CausationId`
traces the direct cause. Responses are not events and every transport rejects response publication.

`EndpointIdentityNormalizer` produces invariant lowercase ASCII-alphanumeric keys, ignoring dash,
underscore, dot and whitespace. RabbitMQ, NATS, Kafka and in-memory topology use this one key. A change
from raw resource names is operator-managed: never auto-delete or silently bind both forms.

Lycia ownership is not global broker exclusivity. Independent RabbitMQ queues, NATS groups/durables, or
Kafka groups can consume the same logical stream. Processing is at least once, business effects must be
idempotent, and Kafka ordering is partition-scoped.

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

## Message Topology and Ownership

Lycia derives topology from message contracts and discovered handlers. Applications do not maintain a
manual route registry. `ApplicationId` names a logical service and is identical across every replica; it
must never contain a pod, host, process, or container identity.

### Contract rules

- A command implements exactly one application endpoint marker inheriting `ICommandEndpoint`.
- Endpoint markers are named `I{LogicalOwner}Command`; for example, `IStockServiceCommand` resolves to
  `StockService`. The transformation is centralized in `CommandEndpointResolver` and is ordinal and
  deterministic.
- A command has exactly one handler type in its owning logical application. Multiple instances of that
  handler are valid replicas and are not duplicate registrations.
- An event may have any number of subscriptions. Each `MessageType + HandlerType + ApplicationId`
  combination is an independent logical subscription; replicas share it.
- A response targets the requesting application from canonical `ResponseEndpoint`; obsolete `ReplyTo`
  remains a forwarding compatibility alias. `RequestId`, `CorrelationId`, and
  `SagaId` correlate it without creating per-saga transport resources.

Startup validation rejects missing or multiple endpoint markers, wrong `ApplicationId`, and conflicting
command handler types. Owner matching uses `StringComparison.OrdinalIgnoreCase`; generated names retain
the configured spelling. Errors include the command, handlers, expected owner, and actual application.

### Transport mapping

| Semantic identity | RabbitMQ | NATS JetStream | Kafka |
|---|---|---|---|
| Command address | direct exchange `command.{Type}`; key `{Owner}` | `command.{Owner}.{Type}` | `{prefix}.command.{Owner}.{Type}` |
| Command consumer | `command.{Type}.{ApplicationId}` | durable consumer from the logical command queue | one consumer group from the logical command queue |
| Event address | fanout exchange `event.{Type}` | `event.{Type}` | `{prefix}.event.{Type}` |
| Event consumer | `event.{Type}.{Handler}.{ApplicationId}` | one durable consumer per handler/application | one group per handler/application |
| Response address | direct exchange `response.{Type}`; key `{ResponseEndpoint}` | `response.{ResponseEndpoint}.{Type}` | `{prefix}.response.{ResponseEndpoint}.{Type}` |
| Response consumer | `response.{Type}.{ApplicationId}` | requester durable consumer | requester consumer group |

RabbitMQ queues are durable, non-exclusive, and non-auto-delete. Event routing keys are empty because a
per-type fanout exchange performs distribution. Commands never use `#`; their queue identity excludes the
handler class, so renaming a handler does not create a new queue.

JetStream is the NATS default and uses explicit acknowledgments, bounded redelivery, and durable consumers.
Core NATS can be selected for intentionally ephemeral workloads only; it cannot retain a saga command while
subscribers are absent.

Kafka commits an offset only after listener acknowledgment. Ordering is partition-scoped and the stable key
preference is `CorrelationId`, then `SagaId`, then `MessageId`. Only one consumer in a group processes a
partition at a time, so replicas above the partition count remain idle. Kafka transactions do not remove
the need for application idempotency.

### Replica semantics

Three `StockService` pods all use `ApplicationId = StockService` and share the same command queue or group.
They are competing consumers: one logical handler type, many runtime instances. Values such as
`StockService-1` and `StockService-2` represent different logical applications and therefore create
independent subscriptions. This can duplicate event processing and defeats replica competition.

The topology is intentionally at-least-once. A broker failure after a side effect but before acknowledgment
can redeliver a message, so handlers remain idempotent.

### Migration from handler-derived command routing

Commands must now implement one `I{Owner}Command` marker. Missing markers fail clearly; there is no silent
namespace or handler-name fallback. Existing `Send(command, handlerType, ...)` signatures remain source
compatible, but `handlerType` is correlation/tracing context and no longer determines command transport
routing. `MessagingNamingHelper.GetRoutingKey` and topic-style helpers are obsolete compatibility APIs;
new code uses `GetQueueName` and the message-kind-specific topology components. Events retain handler names
because distinct handlers are distinct subscriptions.

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
  - Command queue: `command.{MessageType}.{ApplicationId}`; direct binding key is the marker-derived owner.
  - Event queue: `event.{MessageType}.{HandlerType}.{ApplicationId}`; the per-type exchange is fanout.
  - Response queue: `response.{MessageType}.{ApplicationId}`; direct binding and publish keys target the requester.
  - Exchange names remain `{event|command|response}.{MessageType}`.
  - Keep `ApplicationId` unique per logical service and identical across its replicas.

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
