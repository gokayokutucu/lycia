<p align="center">
  <img src="assets/transparent_logo.png" alt="Lycia Logo" width="220">
</p>

# Lycia

[![NuGet](https://img.shields.io/nuget/v/Lycia.svg)](https://www.nuget.org/packages/Lycia)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Lycia.svg)](https://www.nuget.org/packages/Lycia)
![Target Framework](https://img.shields.io/badge/.NET-netstandard2.0%20%7C%20net8.0%20%7C%20net9.0%20%7C%20net10.0-blue)
[![Build](https://github.com/gokayokutucu/lycia/actions/workflows/dotnet.yml/badge.svg)](https://github.com/gokayokutucu/lycia/actions/workflows/dotnet.yml)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![GitHub release](https://img.shields.io/github/v/release/gokayokutucu/lycia)](https://github.com/gokayokutucu/lycia/releases)

**Lycia** is a message-driven saga framework for .NET applications.

It provides:

- coordinated sagas for orchestration and reactive sagas for choreography
- durable saga state with optimistic concurrency and compensation tracking
- strongly typed command ownership and asynchronous targeted responses
- RabbitMQ, NATS and Kafka transports
- SagaStore providers for Redis, SQL Server, PostgreSQL and in-memory testing
- an optional Inbox (duplicate-delivery suppression) and Outbox (durable outgoing intent)
- a service-local atomic persistence boundary for SQL Server and PostgreSQL
- Split Store: relational canonical state with a rebuildable Redis operational projection
- a canonical transition journal with deterministic rebuild and verification
- transport-independent durable scheduling
- a configurable middleware pipeline and OpenTelemetry tracing

Lycia is designed for distributed systems where workflows span multiple services, messages may be
delivered more than once, replicas process work concurrently and failures can occur between individual
steps.

**Lycia follows at-least-once delivery semantics. It does not claim exactly-once processing.** Handlers
and external side effects must remain idempotent. The Inbox, the Outbox and the atomic persistence
boundary narrow the windows in which duplicates or lost intent can occur; none of them changes that
fundamental guarantee.

For architecture, internals and contributor documentation, see [DEVELOPERS.md](DEVELOPERS.md).

---

## Packages

| Package | Purpose |
| --- | --- |
| `Lycia` | Core saga runtime: handlers, dispatching, saga context, compensation, the Outbox dispatcher, middleware and the in-memory SagaStore/event bus used for tests |
| `Lycia.Extensions` | The `AddLycia` registration DSL, configuration, middleware slots, retry, serialization, persistence-boundary resolution, Split Store and reliability diagnostics |
| `Lycia.Extensions.RabbitMq` | RabbitMQ transport: event bus, listener, topology, dead-lettering and the TTL + DLX scheduling strategy |
| `Lycia.Extensions.Nats` | NATS JetStream (default) and Core NATS transport |
| `Lycia.Extensions.Kafka` | Kafka transport |
| `Lycia.Extensions.Scheduling` | Durable, transport-independent scheduling: dispatch worker, Redis and in-memory schedule stores, leases and fencing, vacuum |
| `Lycia.Extensions.OpenTelemetry` | OpenTelemetry tracing and W3C trace-context propagation |
| `Lycia.Persistence.InMemory` | In-memory SagaStore, Inbox, Outbox and journal registration. Tests and local development only — not durable |
| `Lycia.Persistence.Redis` | Redis SagaStore, Inbox and Outbox, and the Split Store operational projection |
| `Lycia.Persistence.SqlServer` | SQL Server SagaStore, Inbox, Outbox, reconciliation and journal stores with embedded schema migration |
| `Lycia.Persistence.PostgreSql` | PostgreSQL SagaStore, Inbox, Outbox, reconciliation and journal stores with embedded schema migration |

Every package targets `netstandard2.0`, `net8.0`, `net9.0` and `net10.0`, except
`Lycia.Persistence.PostgreSql`, which targets `net8.0`, `net9.0` and `net10.0`.

`Lycia.Extensions` never depends on a transport, scheduling or persistence-provider package. Each of
those packages contributes its own methods to the shared DSL builders (for example
`Lycia.Extensions.RabbitMq` adds `.RabbitMq()` to `UseTransport()`), so IntelliSense only shows what is
actually installed.

---

## Installation

```bash
dotnet add package Lycia
dotnet add package Lycia.Extensions
```

Add one transport:

```bash
dotnet add package Lycia.Extensions.RabbitMq
# or: Lycia.Extensions.Nats / Lycia.Extensions.Kafka
```

Add a persistence provider. A SagaStore provider is required:

```bash
dotnet add package Lycia.Persistence.PostgreSql
# or: Lycia.Persistence.SqlServer / Lycia.Persistence.Redis / Lycia.Persistence.InMemory
```

Optional:

```bash
dotnet add package Lycia.Extensions.Scheduling
dotnet add package Lycia.Extensions.OpenTelemetry
```

---

## Quick Start

Everything is registered through one nested, fluent DSL. The callback boundary finalizes registration,
so there is no `.Build()` call:

```csharp
services.AddLycia(configuration, lycia =>
{
    lycia
        .AddSagas()
            .FromCurrentAssembly();

    lycia
        .UseTransport()
            .RabbitMq();

    lycia
        .UsePersistence()
            .WithPostgreSqlSagaStore(options =>
                options.ConnectionString = configuration.GetConnectionString("Lycia"));
});
```

The DSL is organized by concern:

| Entry point | Configures |
| --- | --- |
| `AddSagas()` | handler discovery (`FromCurrentAssembly()`, `FromAssemblies(...)`) |
| `UseTransport()` | the transport provider (`RabbitMq()`, `Nats(...)`, `Kafka(...)`, or `InMemory()` for tests) |
| `UsePersistence()` | the SagaStore, optional Inbox/Outbox, the persistence boundary and Split Store |
| `AddScheduling()` | durable scheduling (from `Lycia.Extensions.Scheduling`) |
| `AddMiddleware()` | the logging, retry and tracing pipeline |

Transport and persistence selection are always explicit method calls, never configuration strings.
Configuration may still supply values such as connection strings, schema names and timeouts. Selecting
two different transports, or two SagaStore providers, fails at configuration time instead of silently
keeping the last one. If no SagaStore provider is selected, resolving `ISagaStore` throws a
configuration error naming the provider packages.

<details>
<summary>Older flat registration API</summary>

The earlier flat form still compiles as an `[Obsolete]` wrapper around the same registration logic:

```csharp
services
    .AddLycia(configuration)
    .AddSagasFromCurrentAssembly()
    .Build();

services.AddLyciaRabbitMq();
```

`AddLyciaRabbitMq()`, `AddLyciaNats(...)`, `AddLyciaKafka(...)`, `AddLyciaScheduling(...)` and
`AddLyciaInMemoryScheduling(...)` keep working; each obsolete warning names its DSL replacement.

</details>

---

## Saga Models

### Coordinated saga

A coordinated saga uses a central orchestrator and durable `TSagaData`. Use it when workflow order must
be explicit, responses determine subsequent commands, compensation must follow a controlled path, and
workflow state must survive process restarts and be continued by any replica.

```csharp
public sealed class CreateInvoiceSagaHandler
    : StartCoordinatedSagaHandler<CreateInvoiceCommand, CreateInvoiceSagaData>
{
    public override async Task HandleStartAsync(
        CreateInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        await Context.Send(
            new ReserveCreditCommand { InvoiceId = command.InvoiceId },
            cancellationToken);

        await Context.MarkAsComplete<CreateInvoiceCommand>(cancellationToken);
    }
}
```

A saga's first handler derives from a `Start...` base class and implements `HandleStartAsync`; later
steps implement `HandleAsync`. Coordinated sagas persist state between deliveries, so the next step may
run on another process, container or Kubernetes replica.

### Reactive saga

A reactive saga implements choreography: each handler reacts to an event independently, without a
central orchestrator or cross-step `TSagaData`. Use it when services should remain autonomous, several
subscribers react to the same fact, and eventual consistency is acceptable.

```csharp
public sealed class InventorySagaHandler
    : ReactiveSagaHandler<OrderCreatedEvent>,
      ISagaCompensationHandler<PaymentFailedEvent>
{
    public override async Task HandleAsync(
        OrderCreatedEvent message,
        CancellationToken cancellationToken = default)
    {
        await Context.Publish(
            new InventoryReservedEvent { OrderId = message.OrderId },
            cancellationToken);

        await Context.MarkAsComplete<OrderCreatedEvent>(cancellationToken);
    }

    public Task CompensateAsync(
        PaymentFailedEvent message,
        CancellationToken cancellationToken = default)
    {
        InventoryService.ReleaseStock(message.OrderId);
        return Task.CompletedTask;
    }
}
```

### Handler failures

When a handler throws, the saga handler base classes catch the exception, record the step as `Failed`
in the SagaStore and start compensation; the message itself is acknowledged, not redelivered. The
failure is logged as a warning and the handler's trace span is marked as an error with `exception.*`
tags. Cancellation records the step as `Cancelled`.

---

## Strongly Typed Command Ownership

Commands declare one logical owner through an endpoint marker. No queue name, destination string or
handler class name is passed to `Send`.

```csharp
public interface IStockServiceCommand : ICommandEndpoint
{
}

public sealed class ReserveStockCommand : CommandBase, IStockServiceCommand
{
    public Guid OrderId { get; set; }
}
```

The handler belongs to the application that owns the endpoint:

```csharp
public sealed class ReserveStockHandler
    : CoordinatedSagaHandler<ReserveStockCommand, StockSagaData>
{
    public override Task HandleAsync(
        ReserveStockCommand command,
        CancellationToken cancellationToken = default)
    {
        return Context.MarkAsComplete<ReserveStockCommand>(cancellationToken);
    }
}
```

Send the command without routing information:

```csharp
await Context.Send(new ReserveStockCommand { OrderId = orderId }, cancellationToken);
```

`IStockServiceCommand` resolves deterministically to the logical owner `StockService`, and the owning
host must use an equivalent canonical `ApplicationId`. Startup validation rejects commands without an
owner endpoint or with several, handlers registered in the wrong logical application, and several
handler types claiming the same owned command in one application.

Commands represent intentions and have one logical owner. Events represent facts and may have many
subscribers.

---

## Canonical Application Identity

Application identities are normalized with invariant lowercase rules. `StockService`, `stock-service`,
`stock_service`, `STOCK.SERVICE` and `stock service` all normalize to `stockservice`: dashes,
underscores, dots and whitespace are ignored, and at least one alphanumeric character is required.

Every replica of a logical application must use the same `ApplicationId`. Do not encode replica
identity into it — `StockService-1` and `StockService-2` are different logical applications with
independent subscriptions. Correctly configured replicas share the same queue, durable consumer or
consumer group and compete for work:

> One logical handler type, many runtime handler instances.

---

## Transport Semantics

Lycia exposes the same messaging semantics across transports while each transport package implements
its native topology.

| Message kind | RabbitMQ | NATS | Kafka |
| --- | --- | --- | --- |
| Command | Direct exchange and one logical owner queue | Owner subject and one durable consumer | Owner topic and one consumer group |
| Event | Fan-out to one queue per subscription | One durable consumer per subscription | One consumer group per subscription |
| Response | Targeted requester queue | Targeted response subject | Targeted response topic/group |

Example command addresses:

| Transport | Address |
| --- | --- |
| RabbitMQ | Exchange `command.ReserveStockCommand`, queue `command.ReserveStockCommand.StockService`, routing key `StockService` |
| NATS | Subject `command.StockService.ReserveStockCommand` |
| Kafka | Topic `lycia.command.StockService.ReserveStockCommand` |

Lycia never creates a queue, subject, topic or consumer group per saga instance.

### RabbitMQ

```csharp
lycia
    .UseTransport()
        .RabbitMq(); // or .RabbitMq(options => { ... }) for code-first overrides
```

The RabbitMQ package owns the event bus, queue/exchange/binding declaration, dead-lettering,
header normalization and the fixed TTL + DLX scheduling buckets. Consumers expose an explicit readiness
signal once their queues, bindings and consumers are registered, which avoids startup races where a
message could be published before its binding exists.

RabbitMQ publishes are currently reported to the Outbox as `ConfirmationUnknown` rather than
`Published`, because the transport abstraction does not yet await a per-publish broker confirmation for
RabbitMQ. See [Outbox](#outbox) for what that means operationally. This is the current validated
behavior and may be revisited.

### NATS and Kafka

NATS uses JetStream by default, with explicit acknowledgements, bounded redelivery and durable
consumers; Core NATS is available for intentionally ephemeral workloads. Kafka commits an offset only
after the handler acknowledges, and ordering is partition-scoped (partition key `CorrelationId`, then
`SagaId`, then `MessageId`). Kafka's idempotent `acks=all` producer and NATS JetStream report positive
broker acceptance, so Outbox messages sent through them become `Published`.

---

## Asynchronous Targeted Responses

Lycia does not use synchronous RPC-style waiting for saga steps. Responses are asynchronous messages
targeted to the logical application waiting for them:

```csharp
await Context.Respond(
    request,
    new InventoryReservedResponse { OrderId = request.OrderId },
    cancellationToken);
```

A response has its own `MessageId`, preserves the workflow `CorrelationId` and `SagaId`, uses
`RequestId` to identify the request it answers and `ResponseEndpoint` to route back to the requester,
and may be consumed by any replica of the requester application.

Responses must be sent with `Respond`. Publishing an `IResponse` through `Context.Publish` fails
explicitly, because responses are targeted continuations, not broadcast facts. `ReplyTo` remains an
obsolete compatibility alias for `ResponseEndpoint`.

A saga step never depends on the process that sent the preceding message staying alive: if replica A
sends a command and stops, replica B receives the response, loads the saga from the SagaStore and
continues the workflow.

---

## Message Identity

| Field | Meaning |
| --- | --- |
| `MessageId` | Identity of one concrete message. Stable across retry, redelivery and recovery of that same message; used for deduplication, idempotency, replay and logging |
| `RequestId` | A request carries its own `MessageId`; a response carries the `MessageId` of the request it answers |
| `CorrelationId` | Stable identity of the business workflow; may span several sagas |
| `CausationId` | The message that directly caused this one |
| `ParentMessageId` | Saga-step and compensation lineage |
| `SagaId` | Identity of the durable saga instance |
| `ResponseEndpoint` | The logical application waiting for a response |

```text
CreateOrderCommand       MessageId = M1  RequestId = M1  CorrelationId = C1  CausationId = null  ParentMessageId = empty  SagaId = S1
OrderCreatedResponse     MessageId = M2  RequestId = M1  CorrelationId = C1  CausationId = M1    ParentMessageId = M1     SagaId = S1
ReserveInventoryCommand  MessageId = M3  RequestId = M3  CorrelationId = C1  CausationId = M2    ParentMessageId = M2     SagaId = S1
```

A retried, redelivered, Outbox-redispatched or schedule-dispatched message keeps its `MessageId`; a new
`MessageId` always means a new message. Compensation traverses `ParentMessageId`; `CausationId` is for
direct causal tracing and does not replace compensation lineage.

---

## Persistence

### SagaStore

A SagaStore persists saga data and the step log. Exactly one provider is selected:

```csharp
lycia.UsePersistence().WithPostgreSqlSagaStore(options => options.ConnectionString = postgres);
// or .WithSqlServerSagaStore(...), .WithRedisSagaStore(...), .WithInMemorySagaStore()
```

Every provider implements explicit numeric optimistic concurrency (`IVersionedSagaStore`): saving with
an `expectedVersion` that no longer matches throws `SagaConcurrencyException` instead of overwriting a
concurrent writer. SQL Server and PostgreSQL use `UPDATE ... WHERE Version = @expected`; Redis uses an
atomic Lua compare-and-set. SQL Server and PostgreSQL apply their own schema through an embedded
migration.

### Inbox

The optional Inbox suppresses duplicate handler execution. Before a handler runs, the dispatcher claims
the `(MessageId, HandlerType)` pair; a redelivery of a message that is already being processed, has
completed, or recently failed is skipped instead of running the handler again.

```csharp
lycia.UsePersistence()
    .WithPostgreSqlSagaStore(options => options.ConnectionString = postgres)
    .WithPostgreSqlInbox(options =>
    {
        options.ConnectionString = postgres;
        options.ClaimRecoveryTimeout = TimeSpan.FromMinutes(5); // default
    });
```

Providers: `.WithInMemoryInbox()`, `.WithRedisInbox(...)`, `.WithSqlServerInbox(...)`,
`.WithPostgreSqlInbox(...)`. The Inbox is disabled unless selected.

- **`Completed` is suppressed permanently**, however old it is. This is the duplicate protection the
  Inbox exists for.
- **`Processing` and `Failed` claims are recovered.** A claim left in `Processing` by a process that
  died mid-handler, or a `Failed` record, becomes claimable again once it is older than
  `ClaimRecoveryTimeout`. Without this, the first crash would turn every later redelivery into a
  permanent no-op. Set the window longer than your slowest handler, because a shorter one lets a
  redelivery take over a claim whose first execution is still running.
- **Takeover is atomic** in every provider — a predicated `UPDATE` for SQL Server and PostgreSQL, a Lua
  script for Redis — so concurrent redeliveries cannot all take over the same stale claim.
- **Under `LocalAtomic`** the claim is part of the handler's transaction, so a crash or exception rolls
  the claim back with everything else. The recovery window matters where the claim commits on its own:
  Redis, InMemory, or relational stores resolving to `Independent`.

The Inbox complements, and does not replace, the SagaStore's own per-step duplicate and transition
checks.

### Outbox

The optional Outbox durably captures outgoing intent. With an Outbox selected, `Context.Send`,
`Context.Publish`, `Context.Respond` and their tracked variants write an Outbox record instead of
publishing directly, and a hosted `OutboxWorker` later restores the original operation through the
transport.

```csharp
lycia.UsePersistence()
    .WithPostgreSqlSagaStore(options => options.ConnectionString = postgres)
    .WithPostgreSqlOutbox(options => options.ConnectionString = postgres)
    .WithOutboxWorker(options =>
    {
        options.BatchSize = 50;
        options.MaxAttempts = 5;
        options.RecoveryTimeout = TimeSpan.FromMinutes(1); // longer than the transport publish timeout
        options.RetryBackoff = TimeSpan.FromMilliseconds(250);
    });
```

Providers: `.WithInMemoryOutbox()`, `.WithRedisOutbox(...)`, `.WithSqlServerOutbox(...)`,
`.WithPostgreSqlOutbox(...)`. Claiming is safe under concurrent workers and replicas: each message is
claimed by exactly one worker at a time. The record's `MessageId` is stable, so a redispatch never
creates a new logical message.

**Lifecycle**

| Status | Meaning |
| --- | --- |
| `Pending` | Captured durably; not yet claimed |
| `Claimed` | Claimed by a worker for dispatch |
| `Publishing` | A publish attempt is in flight |
| `Published` | The transport positively confirmed the publish (Kafka, NATS JetStream) |
| `ConfirmationUnknown` | The publish may have succeeded but was not confirmed — the transport cannot confirm (RabbitMQ, Core NATS) or the attempt threw. Never auto-promoted to `Published` |
| `Failed` | A permanent local error before any publish, such as an unresolvable message type or an invalid envelope. Not retried |
| `Abandoned` | Terminal: the last permitted attempt never reached the transport, or workers stopped on both the final attempt and its recovery attempt. Needs operator attention |

**Bounded retry.** A `ConfirmationUnknown` message becomes eligible for another attempt only after
`RecoveryTimeout` has elapsed, so attempts are spread over roughly `MaxAttempts × RecoveryTimeout`
rather than consumed back to back. Claims left in `Claimed` or `Publishing` by a crashed worker are
recovered after the same window. Recovery can duplicate a publish whose original worker was only slow,
which is part of the at-least-once contract.

**If a worker stops mid-dispatch.** A worker that crashes, or is stopped, after starting an attempt but
before recording its outcome leaves the message in `Publishing`. That includes the last permitted
attempt. After `RecoveryTimeout` another worker picks it up. Lycia cannot know whether the broker
accepted the message, so it publishes it once more with the same `MessageId`: the worst case is a
duplicate, never a silent loss. That recovery publish is the only attempt allowed beyond `MaxAttempts`,
so a message is started at most `MaxAttempts + 1` times and only when a worker died on it. If the
recovery attempt is lost as well, the message becomes `Abandoned` instead of looping.

**When attempts run out**, the outcome depends on the last attempt:

- **It never reached the transport** — the publish threw, for example because the broker was down. The
  message becomes `Abandoned`, the reason is recorded in its failure info,
  and a warning naming the `MessageId` and `SagaId` is logged. `OutboxDispatchResult.Abandoned` carries
  the count, so you can alert on it. The message is not dispatched again automatically.
- **The transport accepted it but cannot confirm it** — the normal case for RabbitMQ and Core NATS. The
  message stays `ConfirmationUnknown` and is not dispatched again. It was handed to the broker on each
  attempt, so this is ordinary at-least-once delivery, not a failure, and raises no warning.

Because an unconfirming transport receives the same message on every attempt, consumers must be
idempotent — the Inbox handles exactly this on the receiving side.

**Scheduling and the Outbox.** Scheduling owns not-yet-due intent. When a schedule becomes due, it hands
the original `Send`/`Publish`/`Respond` to the same outgoing pipeline, so with an Outbox enabled a due
schedule creates one Outbox record rather than two competing durable records.

### Atomic persistence boundary

The persistence-boundary policy defaults to `Auto`. When the SagaStore, Inbox and Outbox are all SQL
Server, or all PostgreSQL, and resolve to the same logical database, they share one service-local
transaction (`LocalAtomic`). Different schemas in that database may participate; different databases
or mixed providers resolve to `Independent`.

```csharp
lycia.UsePersistence()
    .WithPostgreSqlSagaStore(options => options.ConnectionString = postgres)
    .WithPostgreSqlInbox(options => options.ConnectionString = postgres)
    .WithPostgreSqlOutbox(options => options.ConnectionString = postgres);
// Auto -> LocalAtomic
```

- `.RequireAtomicBoundary()` makes an incompatible topology fail at startup.
- `.UseIndependentTransactions()` deliberately opts out when a compatible boundary exists.

The transaction covers only the enabled Lycia Inbox, SagaStore and Outbox operations of one service. It
never spans services, never uses a distributed transaction, and does not include application business
tables. Outbox publication happens after commit. If a COMMIT is issued but its outcome cannot be
observed, Lycia reports an unknown outcome rather than rerunning the handler; the durable identities
(Inbox claim, saga version, Outbox `MessageId`) are the recovery authority. InMemory and Redis stores
are always `Independent`.

### Split Store

Split Store makes PostgreSQL or SQL Server the canonical store and Redis an asynchronously reconciled,
rebuildable operational projection:

```csharp
lycia.UsePersistence()
    .WithPostgreSqlCanonicalSagaStore(options => options.ConnectionString = postgres)
    .WithPostgreSqlInbox(options => options.ConnectionString = postgres)
    .WithPostgreSqlOutbox(options => options.ConnectionString = postgres)
    .WithRedisOperationalSagaStore(options => options.ConnectionString = redis)
    .RequireAtomicBoundary()
    .UseSplitStore();
```

- **Relational state is canonical.** The Inbox claim, canonical saga state, Outbox record, a
  reconciliation intent and a journal entry commit in one service-local transaction. Redis is never
  written before that commit, and handler reads always use canonical state.
- **Reconciliation** installs the committed state in Redis in the background
  (`WithReconciliationWorker(...)` tunes batching and bounded retry with backoff and jitter).
- **Version fencing.** Redis installs use compare-and-set on the saga version: an equal version is a
  no-op and an older version is superseded, so a delayed or duplicated install can never overwrite a
  newer projection.
- **Redis outages do not stop the workflow.** Canonical commits and Outbox dispatch continue while Redis
  is down, and reconciliation repopulates the projection when it returns.

### Canonical journal, rebuild and verify

Split Store requires an append-only canonical journal (`ISagaJournalStore`), registered automatically by
`With...CanonicalSagaStore`. Each committed transition appends one immutable entry, ordered by `SagaId`
and `SequenceNumber` (equal to the saga version), in the same transaction as the canonical write.

`ISagaRebuildService` rebuilds the Redis projection from the journal and verifies it:

- `RebuildSagaAsync` / `RebuildAllAsync` — bounded pages, per-saga failure isolation, progress
  reporting, cancellation and a resumable cursor.
- `VerifySagaAsync` / `VerifyAllAsync` — non-mutating classification: `Healthy`, `MissingProjection`,
  `VersionMismatch`, `StateMismatch`, `JournalGap`, `SchemaUnsupported`, `CorruptEntry`.

Rebuild is deterministic and never invokes handlers, publishes messages or writes the Inbox or Outbox.
It installs through the same version-fenced writer as reconciliation, so a stale rebuild cannot
overwrite a newer projection and rebuilding twice is a safe no-op. Gaps and corrupt entries are reported,
never silently reconstructed, and older journal schema versions are upgraded through registered
`IJournalEntryUpcaster`s.

### Reliability diagnostics

`AddLycia` registers `ILyciaReliabilityDiagnostics`, a safe snapshot of the active persistence topology:

```csharp
var snapshot = serviceProvider
    .GetRequiredService<ILyciaReliabilityDiagnostics>()
    .GetSnapshot();

// snapshot.Mode, snapshot.CanonicalStore, snapshot.OperationalStore, snapshot.ResolvedStrategy,
// snapshot.ReconciliationEnabled, snapshot.JournalEnabled, snapshot.JournalRebuildAvailable,
// snapshot.InboxEnabled, snapshot.OutboxEnabled, snapshot.DeliveryGuarantee ("AtLeastOnce")
```

It never contains connection strings, credentials or payloads. Use it for a startup log line or a
diagnostics endpoint.

---

## Durable Message Scheduling

```bash
dotnet add package Lycia.Extensions.Scheduling
```

Safe defaults apply, so the minimal registration needs no tuning:

```csharp
lycia
    .AddScheduling()
        .WithRedisStore()
        .WithPredefinedDelays();
```

`WithPredefinedDelays()` allows only the predefined `ScheduleDelay` buckets; `WithDynamicDelays()` also
allows arbitrary durations. Tune dispatch and enable vacuum only when the defaults do not fit:

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
        .WithVacuum(options =>
        {
            options.ApplicationTopology.Mode = VacuumMode.ReportOnly;
        });
```

`WithDispatch(...)` configures batching, claim lifetime, lease renewal and bounded retry with backoff
and jitter for due-schedule dispatch. (`WithWorker(...)` remains as an `[Obsolete]` alias.)
`WithInMemoryStore()` is available for tests.

Schedule a message from a saga context:

```csharp
var scheduleId = await Context.Schedule(
    new CancelOrderCommand { OrderId = orderId },
    ScheduleDelay.ThirtySeconds,
    cancellationToken);
```

`ScheduleId` identifies the scheduling operation and is deliberately different from `MessageId`; pass a
stable `ScheduleId` when retrying schedule creation. Pending schedules can be cancelled or rescheduled
idempotently before dispatch.

- **RabbitMQ** predefined delays use one fixed-TTL queue per destination and bucket, dead-lettered to
  the final destination; no `x-delayed-message` plugin is needed. Dynamic delay queues are opt-in.
- **Kafka and NATS** use the durable dispatch worker; topic retention is never treated as delayed
  delivery.

Due schedules are claimed under a lease protected by a fencing token (owner and fence are both
validated, not lease expiry alone), so a stale or resumed owner cannot take work another live owner
holds. Dispatch is at least once around crash and confirmation windows; consumers must be idempotent.

Vacuum deletes dynamic scheduling resources only with exact registry provenance, no active schedule or
manifest, empty and unused broker resources, lease ownership and fencing. Predefined delay buckets are
kept, and ordinary application topology is report-only unless destructive cleanup is explicitly enabled.

---

## Middleware

```csharp
lycia
    .AddMiddleware()
        .WithLogging()
        .WithRetry(options => options.MaxRetryAttempts = 5)
        .WithTracing();
```

The default pipeline has logging, retry and tracing slots. Replace an implementation with the generic
form (`WithLogging<TMiddleware>()`, `WithRetry<TMiddleware>()`, `WithTracing<TMiddleware>()`), which
targets `ILoggingSagaMiddleware`, `IRetrySagaMiddleware` and `ITracingSagaMiddleware`. The default
tracing implementation is `ActivityTracingMiddleware`.

Retry is provided through `IRetryPolicy`, with a Polly-based default supporting bounded retries,
exponential backoff, jitter and exception-specific policies. Retries do not replace the Inbox, the
Outbox or idempotent handlers.

---

## OpenTelemetry Tracing

```bash
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package Lycia.Extensions.OpenTelemetry
```

```csharp
services
    .AddOpenTelemetry()
    .AddLyciaTracing()
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddOtlpExporter(options =>
            options.Endpoint = new Uri("http://otel-collector:4317"));
    });
```

`AddLyciaTracing()` registers the Lycia activity source and the W3C `traceparent`/`tracestate`
propagator. Trace context flows through RabbitMQ, NATS and Kafka headers and through the Outbox, so a
workflow that crosses services and Outbox hops appears as one connected trace, with each handler span a
child of the message that caused it.

Handler spans carry these attributes:

```text
lycia.saga.id             lycia.message.id           lycia.correlation.id
lycia.causation_id        lycia.parent_message_id    lycia.request_id
lycia.handler             lycia.application.id       lycia.saga.step.status
```

`lycia.saga.step.status` is `Completed` or `Failed`; a failed step also sets the span status to error
with `exception.type` and `exception.message`. Outbox dispatch adds an `Outbox.Send`,
`Outbox.Publish` or `Outbox.Respond` producer span. Export to any OpenTelemetry-compatible backend
such as Jaeger or Grafana Tempo.

Long-running workflows should rely on Lycia's durable identifiers (`CorrelationId`, `SagaId`,
`CausationId`, `ParentMessageId`) rather than assuming one open trace represents the whole business
workflow.

---

## Idempotency and Concurrency

Idempotency and concurrency are separate concerns.

**Idempotency** prevents duplicate handling of the same message: the same `MessageId` delivered twice
should produce one committed business effect. The Inbox enforces this at the handler boundary; handler
code and external integrations should still use stable identities and idempotent operations.

**Optimistic concurrency** prevents different valid messages from overwriting the same saga state. If
two replicas load version 7 while processing different messages, only one of them can commit
version 8.

---

## Extensibility

Applications and extension packages can provide their own implementations of `IEventBus`, `ISagaStore`,
`IInboxStore`, `IOutboxStore`, `IMessageSerializer`, middleware slots, retry policies, tracing
integrations, and scheduling stores and strategies. The generic `UsePersistence().WithInbox<T>()` and
`.WithOutbox<T>()` register custom Inbox and Outbox stores with the same duplicate-provider guard as the
built-in providers.

---

## Samples

The [samples/](samples) directory contains runnable examples.

| Sample | Purpose |
| --- | --- |
| `Sample.Order.Api` | API entry point and initial command submission |
| `Sample.Order.Orchestration.Consumer` | Stateful coordinated saga with asynchronous targeted responses |
| `Sample.Order.Choreography.Consumer` | Stateless event-driven reactive saga |
| `Sample.Order.Orchestration.Seq.Consumer` | Sequential coordinated saga with compensation |
| [`Microservices`](samples/Microservices) | Five services on RabbitMQ, PostgreSQL, per-service Redis and Jaeger, using Split Store, Inbox, Outbox and the journal. See its [README](samples/Microservices/README.md) and [manual testing guide](samples/Microservices/MANUAL_TESTING.md) |

---

## Roadmap

Deferred work, not available today:

- **RabbitMQ publish confirmation.** Reporting RabbitMQ publishes as `Published` requires awaiting a
  per-publish broker confirmation in the transport; until then RabbitMQ stays `ConfirmationUnknown`.
- **Redis Cluster.** The Redis Inbox/Outbox scripts touch several keys without hash tags, so they
  target standalone (non-clustered) Redis.
- **Journal acceleration and tracking.** A durable snapshot table, and persistent, queryable
  rebuild-operation tracking (a resumable cursor exists today).
- **Schema registry integration** for Avro/Protobuf serialization.
- **Workflow explorer.** Visualizing commands, responses, service boundaries and compensation paths from
  OpenTelemetry spans, message identities, saga state and the journal.

---

## Design Principles

- commands have one logical owner; events may have many subscribers
- responses are targeted, not broadcast
- workflows continue asynchronously, and process memory is never the durable workflow boundary
- replicas share logical application identity
- delivery is at least once, handlers remain idempotent, and retries are bounded
- transport behavior stays outside the core
- operational guarantees are documented without exactly-once claims

---

## Project History

Lycia began on **May 28, 2023** with the goal of making distributed saga workflows easier to model,
operate and understand. The name is inspired by the Lycian Way and the idea of turning difficult paths
into understandable routes.

---

## License

Lycia is licensed under the [Apache License 2.0](LICENSE).
