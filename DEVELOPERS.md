# Lycia Developer Documentation

This document provides an in-depth look into the architecture, components, configuration, and internals of the Lycia Saga Infrastructure. It complements the public-facing `README.md` with implementation details, design decisions, and extensibility points.

---

## Transport-independent scheduling

Scheduling contracts live in `Lycia.Saga.Abstractions`; scheduling orchestration, the deterministic in-memory
components, and the Redis schedule store live in `Lycia.Extensions.Scheduling`; the RabbitMQ TTL+DLX native
strategy lives in `Lycia.Extensions.RabbitMq`. Kafka and the supported NATS baseline use the same
durable-worker path, keeping transport-specific behavior out of the core scheduler.

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

## 🗃 SagaStore Providers

Four `ISagaStore` provider packages exist, each selected explicitly via
`lycia.UsePersistence().With...SagaStore(...)` — never by a configuration string. Exactly one may be
selected per application; a second selection throws `InvalidOperationException` at configuration
time (`LyciaPersistenceBuilder.SelectProvider`).

- **`Lycia.Persistence.InMemory`** (`InMemorySagaStore`, in the `Lycia` core package) — a
  lightweight, non-persistent store for unit tests and local development. Uses a composite key of
  `SagaId`, `MessageId`, and `HandlerType`; all step metadata is stored in memory dictionaries.
  Not durable production storage.
- **`Lycia.Persistence.Redis`** (`RedisSagaStore`) — the production Redis-backed store, moved out of
  `Lycia.Extensions` into its own package so `Lycia.Extensions` never depends on a concrete
  persistence provider.
- **`Lycia.Persistence.SqlServer`** / **`Lycia.Persistence.PostgreSql`** — relational stores with an
  embedded schema migration (`dbo.LyciaSagaData`/`dbo.LyciaSagaSteps`, or `lycia_saga_data`/
  `lycia_saga_steps` for PostgreSQL), explicit `BIGINT`/`bigint` version columns, and
  transactional, parameterized ADO.NET access (no `MERGE`, no opaque `rowversion`/`xmin` as the
  public version).

All four implement `IVersionedSagaStore` for explicit numeric optimistic concurrency:

```csharp
long newVersion = await versionedStore.SaveSagaDataAsync(sagaId, data, expectedVersion: currentVersion);
```

A mismatched `expectedVersion` throws `SagaConcurrencyException` (`Lycia.Saga.Exceptions`) instead of
silently overwriting a concurrent writer's update. Redis implements this as a single atomic Lua
`EVAL` (compare-and-set); SQL Server/PostgreSQL implement it as
`UPDATE ... SET Version = Version + 1 WHERE SagaId = @id AND Version = @expected` plus a rows-affected
check. All four providers run the same shared `Lycia.Persistence.TestKit` conformance suite
(`tests/Lycia.Persistence.TestKit`), so step-transition validation, idempotency, and concurrency
semantics are identical across providers.


## 📥📤 Inbox / Outbox

Durable providers exist for InMemory, Redis, SQL Server, and PostgreSQL — see the Roadmap section of
README.md for what's still planned beyond this.

- **`IInboxStore`** (`Lycia.Saga.Abstractions.Inbox`) — tracks committed processing identity of
  incoming messages, keyed by `(MessageId, HandlerType)`. Distinct from `ISagaStore`'s per-step
  transition validation: Inbox gates *before* the handler body runs (dedup on message delivery),
  while the SagaStore step log remains the source of truth for saga progress/compensation.
  `SagaDispatcher.InvokeHandlerAsync` calls `TryBeginAsync` right before building the saga context;
  a non-`Started` result (`AlreadyProcessing`/`AlreadyCompleted`/`AlreadyFailed`) short-circuits the
  dispatch as a safe no-op. `MarkCompletedAsync`/`MarkFailedAsync` are called after the handler
  pipeline finishes. `IInboxStore` is resolved optionally (`serviceProvider.GetService<IInboxStore>()`)
  — when nothing is registered, dispatch behaves exactly as it did before Inbox existed.
- **`IOutboxStore`** (`Lycia.Saga.Abstractions.Outbox`) — durably captures outgoing message intent
  (`AddAsync`, idempotent on `MessageId`) with an explicit lifecycle
  (`Pending → Claimed → Publishing → Published/ConfirmationUnknown/Failed`), plus `ClaimPendingBatchAsync`
  for a publisher to atomically take ownership of a batch without another worker claiming the same rows.

### Durable providers

- **`Lycia.Persistence.InMemory`**: `InMemoryInboxStore`/`InMemoryOutboxStore` — deterministic,
  in-memory, tests/local development only.
- **`Lycia.Persistence.Redis`**: `RedisInboxStore` claims via an atomic `SETNX` on a per
  `(HandlerType, MessageId)` key; `RedisOutboxStore` uses Lua scripts for idempotent `AddAsync`
  (`SETNX` + pending-set add in one `EVAL`) and atomic `ClaimPendingBatchAsync` (pop-from-sorted-set +
  status update in one `EVAL`, so concurrent claimers can never race). Targets standalone/non-clustered
  Redis — the Lua scripts touch multiple keys (`outbox:msg:{id}`, `outbox:pending`) that are not
  guaranteed to hash to the same slot in a real Redis Cluster without hash-tag key naming, which is
  not implemented here.
- **`Lycia.Persistence.SqlServer`**: `LyciaInbox`/`LyciaOutbox` tables (`002_InboxOutboxSchema.sql`,
  applied only when Inbox/Outbox is actually enabled, not by `WithSqlServerSagaStore` alone).
  `SqlServerInboxStore.TryBeginAsync` uses transaction-aware locking and the store's unique identity
  constraint. `SqlServerOutboxStore.ClaimPendingBatchAsync` uses
  `UPDATE TOP (@n) ... OUTPUT INSERTED.* WHERE Status = Pending`, safe under concurrent callers via
  SQL Server's normal row locking during the `UPDATE`.
- **`Lycia.Persistence.PostgreSql`**: `lycia_inbox`/`lycia_outbox` tables with `jsonb` payload/failure
  columns (`002_InboxOutboxSchema.sql`, same enable-only-when-used rule).
  `PostgreSqlOutboxStore.ClaimPendingBatchAsync` uses the classic
  `SELECT ... FOR UPDATE SKIP LOCKED` pattern so two concurrent claimers never select the same row.

### DSL

`UsePersistence().WithInbox<T>()` / `.WithOutbox<T>()` are generic escape hatches on
`LyciaPersistenceBuilder` (same shape as `LyciaBuilder.UseSagaStore<T>()`), each with its own
duplicate-provider guard (`SelectInboxProvider`/`SelectOutboxProvider`, mirroring `SelectProvider`'s
marker-in-`IServiceCollection` pattern). Provider packages add named sugar instead of calling the
generic method directly, mirroring their SagaStore DSL exactly: `.WithInMemoryInbox()`/`.WithInMemoryOutbox()`,
`.WithRedisInbox()`/`.WithRedisOutbox()`, `.WithSqlServerInbox()`/`.WithSqlServerOutbox()`,
`.WithPostgreSqlInbox()`/`.WithPostgreSqlOutbox()`. Both remain optional and disabled by default.

### Outbox dispatcher

`IOutgoingMessagePipeline` is the single direct-versus-durable selection point. All saga contexts,
including tracked adapters and due-schedule dispatch, call it. `DirectOutgoingMessagePipeline`
delegates unchanged to `IEventBus`; `OutboxOutgoingMessagePipeline` uses `IMessageSerializer` and
stores a versioned `OutboxEnvelope` containing stable identity, operation (`Send`, `Publish`, or
`Respond`), body/headers, handler/application/saga routing, and the original request body needed for
targeted response routing. Schedule is deliberately not an Outbox operation: the schedule store owns
the intent until due, then hands the original semantic to this pipeline.

`OutboxWorker` is registered with every `.With...Outbox()` provider and configured with
`.WithOutboxWorker(...)`. It creates a scope per pass, atomically claims batches, honors shutdown
cancellation, recovers expired `Claimed`/`Publishing` work after `RecoveryTimeout`, and uses bounded
attempts with exponential backoff and jitter. `RecoveryTimeout` must exceed the transport publish
timeout; a slow original worker can still create the documented at-least-once duplicate window. A stable `MessageId`
also serves as `OutboxId`, so retry/recovery cannot create a new logical message. Concurrent replicas
rely on provider claim atomicity; Outbox intentionally has no scheduler-style fencing token because
claim ownership is a different, shorter lifecycle.

`OutboxDispatcher` restores the operation rather than publishing everything. Only
`IConfirmedEventBus` success becomes `Published`: Kafka (`EnableIdempotence`, `Acks.All`) and NATS
JetStream provide this capability. Core NATS and the current RabbitMQ publisher remain
`ConfirmationUnknown`; they are safely redispatchable and therefore at least once. A transport
exception is also `ConfirmationUnknown`; only a permanent local envelope/type/serialization error is
`Failed`.

### Atomic persistence session

- **`ILyciaPersistenceSession`/`ILyciaPersistenceSessionFactory`** (`Lycia.Saga.Abstractions.Persistence`)
  — the provider-neutral, service-local SagaStore+Inbox+Outbox transaction boundary.
  `RelationalPersistenceSession`/`RelationalPersistenceSessionFactory`
  (`Lycia.Persistence.Relational.Internal.Sessions`) wrap a real `DbConnection`/`DbTransaction`, and
  are registered by `WithSqlServerSagaStore(...)`/`WithPostgreSqlSagaStore(...)` using
  `SqlConnection`/`NpgsqlConnection` respectively (`SupportsAtomicTransactions = true`).
  A scoped `ILyciaPersistenceSessionAccessor` gives relational stores explicit access to the session
  owned by `SagaDispatcher`; it is not static or `AsyncLocal`. The dispatcher claims Inbox, runs the
  handler, captures Outbox intent, persists Saga state/steps, marks Inbox completed, and then commits.
  A failure rolls back partial writes. An unobservable commit result becomes
  `PersistenceCommitOutcomeUnknownException` and is not reclassified as a definite rollback.

`Auto` is the default policy. Matching SQL Server or PostgreSQL database identities resolve
`LocalAtomic`; mixed providers or different databases resolve `Independent`. The public assertions
are `.RequireAtomicBoundary()` and `.UseIndependentTransactions()`. The boundary never spans
services, does not automatically include application business tables, and does not change Lycia's
at-least-once delivery model.


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
      "ConnectionString": "amqp://guest:guest@127.0.0.1:5672/"
    },
    "EventStore": {
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

Configuration only ever supplies values (connection strings, retry counts, TTLs). It never selects a
transport or SagaStore provider by itself — the `EventBus`/`EventStore` sections above are bound into
`EventBusOptions`/`SagaStoreOptions`, but the actual provider is chosen explicitly in code:

```csharp
services.AddLycia(configuration, lycia =>
{
    lycia.UseTransport().RabbitMq();
    lycia.UsePersistence().WithRedisSagaStore(options =>
        configuration.GetSection("Lycia:EventStore").Bind(options));
});
```

---

## 🔮 Roadmap

- Inbox/Outbox providers, outgoing capture, semantic dispatcher, hosted worker, Kafka/JetStream
  confirmation integration, and SQL Server/PostgreSQL atomic Lycia persistence boundaries are
  complete. RabbitMQ publisher confirms remain a transport-specific follow-up.
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

The canonical registration entry point nests configuration by concern while staying fluent. The
callback boundary itself finalizes registration (`Build()` runs internally), so no separate
`.Build()` call is needed:

```csharp
services.AddLycia(configuration, lycia =>
{
    lycia
        .AddSagas()
            .FromAssemblies(typeof(SomeHandler).Assembly);

    lycia
        .UseTransport()
            .RabbitMq(); // Nats()/Kafka()/InMemory() are equivalent alternatives

    lycia
        .UsePersistence()
            .WithRedisSagaStore();

    lycia
        .AddScheduling()
            .WithRedisStore()
            .WithPredefinedDelays()
            .WithWorker(options =>
            {
                options.LeaseDuration = TimeSpan.FromSeconds(30);
                options.LeaseRenewInterval = TimeSpan.FromSeconds(10);
            });

    lycia
        .AddMiddleware()
            .WithLogging()
            .WithRetry()
            .WithTracing();

    lycia.UseMessageSerializer<CustomSerializer>();
});
```

Each nested builder is a small concern-specific type (`LyciaSagaBuilder`, `LyciaTransportBuilder`,
`LyciaPersistenceBuilder`, `LyciaMiddlewareBuilder`; `LyciaSchedulingBuilder` is contributed by
`Lycia.Extensions.Scheduling`), so IntelliSense after `UseTransport()` only shows the transport
providers actually referenced (`RabbitMq()`, `Nats()`, `Kafka()`, `InMemory()`), not every Lycia
method. `Lycia.Extensions` itself only defines `LyciaTransportBuilder`/`LyciaPersistenceBuilder`
(plus the transport-side `InMemory()` provider and each builder's duplicate-provider guard) —
transport, scheduling, and persistence-provider packages (`Lycia.Persistence.InMemory`, `.Redis`,
`.SqlServer`, `.PostgreSql`) extend those builder types with their own extension methods, so
`Lycia.Extensions` never takes a compile-time dependency on `Lycia.Extensions.RabbitMq`, `.Nats`,
`.Kafka`, `.Scheduling`, or any concrete persistence-provider package. Selecting two different
transport providers on one registration (`UseTransport().RabbitMq()` then `UseTransport().Nats()`)
throws `InvalidOperationException` naming both providers, instead of the second call silently
winning — `UsePersistence()` enforces the same rule for SagaStore providers.

- Builder APIs (`LyciaBuilder`, still available directly for callers that don't use the nested
  form): `UseMessageSerializer<T>()`, `UseEventBus<T>()`, `UseSagaStore<T>()`,
  `AddSagasFromCurrentAssembly()`, `AddSagasFromAssemblies(...)`, `ConfigureSaga(...)`, etc.
- The older flat form (`services.AddLycia(configuration).AddSagasFromCurrentAssembly().Build();`
  followed by `services.AddLyciaRabbitMq();`) still compiles; `AddLyciaRabbitMq()`,
  `AddLyciaNats(...)`, `AddLyciaKafka(...)`, `AddLyciaScheduling(...)`, and
  `AddLyciaInMemoryScheduling(...)` are now `[Obsolete]`-marked thin wrappers around the same
  registration logic the DSL calls, each naming its DSL replacement in the warning.

### Queue Type Map

- `_LyciaHandlerDiscovery` resolves types:
  - `SafeGetTypes`, `IsSagaHandlerBase`, `ImplementsAnySagaInterface`
  - `GetMessageTypesFromHandler()` analyzes interface and base classes

---


---
