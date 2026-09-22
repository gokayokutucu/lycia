# Lycia Developer Documentation

This document describes how Lycia works internally, how to develop and test it, and how it is released.
It complements the user-facing [README.md](README.md). [PROJECT_LEDGER.md](PROJECT_LEDGER.md) records the
locked architectural decisions, deferred work and release readiness; [AGENTS.md](AGENTS.md) defines the
Git workflow.

---

## Repository and package layout

Twelve packages are published. Four projects are internal and never published on their own:

| Internal project | Shipped inside |
| --- | --- |
| `Lycia.Saga.Abstractions` (contracts: `ISagaStore`, `IInboxStore`, `IOutboxStore`, `IEventBus`, scheduling, journal, reconciliation) | `Lycia` |
| `Lycia.Saga` (handler base classes, `SagaContext`, outgoing pipelines) | `Lycia` |
| `Lycia.Common` (shared enums, messaging primitives, configuration) | `Lycia` |
| `Lycia.Persistence.Relational.Internal` (relational session, connection lease, migration runner, retry helper) | `Lycia.Persistence.SqlServer`, `Lycia.Persistence.PostgreSql` |

The embedding is done with `PrivateAssets="All"` project references plus a
`TargetsForTfmSpecificBuildOutput` target that copies the internal assemblies into each package's `lib`
folder. No public package declares a NuGet dependency on an internal project.

In CI pack mode (`UsePackageRefForLyciaOnPack=true`) the provider, transport and extension packages
replace their project references to `Lycia` and `Lycia.Extensions` with package references pinned to the
exact version being packed (`[x.y.z]`).

**In-memory types** are spread by responsibility:

| Type | Package |
| --- | --- |
| `InMemorySagaStore` (namespace `Lycia.Stores`), `InMemoryEventBus` | `Lycia` |
| `WithInMemorySagaStore()`, `InMemoryInboxStore`, `InMemoryOutboxStore`, `InMemorySagaJournalStore`, `WithInMemoryInbox()`/`WithInMemoryOutbox()` | `Lycia.Persistence.InMemory` |
| `UseTransport().InMemory()` | `Lycia.Extensions` |
| `InMemoryScheduleStore`, `WithInMemoryStore()` | `Lycia.Extensions.Scheduling` |

All in-memory implementations are for tests and local development and are never durable.

**Target frameworks.** Every src project targets `netstandard2.0;net8.0;net9.0;net10.0`, except
`Lycia.Persistence.PostgreSql` (`net8.0;net9.0;net10.0`) and `Lycia.Extensions.AspNetCore`
(`net8.0;net9.0;net10.0` only, plus `FrameworkReference Microsoft.AspNetCore.App` - Minimal API endpoint
routing does not exist for `netstandard2.0`/net48, and no other package takes an ASP.NET Core dependency;
see "Diagnostics endpoint" below). Code must compile on `netstandard2.0`: for example, use `set` rather
than `init` accessors in shipped code (no `IsExternalInit`).

---

## Dispatch pipeline

A delivery flows through these steps:

1. The transport listener receives a message and restores its trace context.
2. `SagaDispatcher` resolves the handler and, when a SQL Server/PostgreSQL `LocalAtomic` boundary is
   active, opens an `ILyciaPersistenceSession` owned by the dispatcher.
3. If an Inbox is registered, `IInboxStore.TryBeginAsync(messageId, handlerType)` claims the pair. Any
   result other than `Started` rolls back the session and returns without running the handler.
4. `SagaContextFactory` builds the saga context; `ISagaJournalContextAccessor` receives the message's
   correlation metadata for journaling.
5. The middleware pipeline (logging, retry, tracing, custom) invokes the handler.
6. The handler's `Send`/`Publish`/`Respond` calls go through `IOutgoingMessagePipeline`: direct to the
   event bus, or into the Outbox when one is registered. Saga state and step log writes go through the
   SagaStore.
7. The Inbox record is marked `Completed`, and the session commits.
8. With an Outbox, `OutboxWorker` later claims the captured records and restores the original operation
   through the transport.

A failure before commit rolls back every enabled Lycia store in the session. An exception that escapes
the pipeline marks the Inbox record `Failed` and propagates to the transport.

### Core components

- **`SagaDispatcher`** — routing, Inbox claim, session ownership, journal context, middleware invocation
  and commit.
- **Handler base classes** (`StartCoordinatedSagaHandler`, `CoordinatedSagaHandler`,
  `StartCoordinatedResponsiveSagaHandler`, `CoordinatedResponsiveSagaHandler`,
  `StartReactiveSagaHandler`, `ReactiveSagaHandler`) — wrap `HandleStartAsync` (start handlers) or
  `HandleAsync` (step handlers). A business exception is
  caught and recorded through `Context.MarkAsFailed`, and cancellation through `MarkAsCancelled`, rather
  than propagating to the transport.
- **`SagaContext`** — identity propagation, step state (`MarkAsComplete`, `MarkAsFailed`,
  `MarkAsCancelled`, `MarkAsCompensated`, `MarkAsCompensationFailed`), saga data and scheduling.
- **`SagaCompensationCoordinator`** — records the failed step, finds the compensation handler and walks
  compensation lineage (`ParentMessageId`) with cycle detection and validated step transitions.

### Handler failure visibility

Because handler base classes turn exceptions into a recorded `Failed` step, the dispatch itself returns
normally. To keep failures visible, `SagaCompensationCoordinator.CompensateAsync` — the one path every
`MarkAsFailed` overload goes through — logs a warning with the saga, message, handler and exception when
it first records a failed step, and marks `Activity.Current` as an error with
`lycia.saga.step.status=Failed`, `lycia.saga.step.failure_reason`, `exception.type` and
`exception.message`. `ActivityTracingMiddleware` only writes `lycia.saga.step.status=Completed` when no
status has been recorded, so it never overwrites that outcome.

---

## Message identity

The identity semantics below are locked.

| Field | Contract |
| --- | --- |
| `MessageId` | Identifies one concrete message. It stays the same across retry, redelivery, Outbox redispatch, schedule dispatch and dead-letter replay of that same message, and is the key for deduplication, idempotency, replay and logging. A new `MessageId` always means a new message |
| `RequestId` | An initial request carries its own `MessageId` as `RequestId`. A response carries the `MessageId` of the request it answers |
| `CorrelationId` | Stable identity of the business workflow. It may span several sagas |
| `CausationId` | The message that directly caused this one |
| `ParentMessageId` | Saga-step and compensation lineage; the only field compensation and bubble-up traverse |
| `SagaId` | Identity of the durable saga instance |

The saga context centralizes propagation:

- **Commands** get a fresh `MessageId`, `RequestId = MessageId`, and the current correlation, saga,
  causation, parent and response endpoint.
- **Responses** get a fresh `MessageId`; `RequestId`, `CausationId` and `ParentMessageId` are the
  request's `MessageId`; `ResponseEndpoint` is the waiting application's canonical endpoint.
- **Events** get workflow and lineage metadata but no request metadata.

The Outbox uses the message's `MessageId` as its `OutboxId`, and the schedule store keeps the original
payload with its `MessageId`, so neither recovery path can mint a new identity for an existing message.
`ScheduleId` identifies a scheduling operation and is deliberately separate from `MessageId`.

---

## Message topology and ownership

Lycia derives topology from message contracts and discovered handlers; applications do not maintain a
route registry. `ApplicationId` names a logical service and is identical across every replica; it must
never contain a pod, host, process or container identity.

### Contract rules

- A command implements exactly one endpoint marker inheriting `ICommandEndpoint`.
- Markers are named `I{LogicalOwner}Command`; `IStockServiceCommand` resolves to `StockService`. The
  transformation is centralized in `CommandEndpointResolver` and is ordinal and deterministic.
- A command has exactly one handler type in its owning application. Several instances of that handler
  are replicas, not duplicate registrations.
- An event may have any number of subscriptions. Each `MessageType + HandlerType + ApplicationId`
  combination is an independent logical subscription shared by its replicas.
- A response targets the requesting application through canonical `ResponseEndpoint`; `ReplyTo` is an
  obsolete forwarding alias. `RequestId`, `CorrelationId` and `SagaId` correlate it without creating
  per-saga transport resources.

Startup validation rejects missing or multiple endpoint markers, a wrong `ApplicationId`, and
conflicting command handler types. Owner matching is `OrdinalIgnoreCase`; generated names keep the
configured spelling. Errors name the command, handlers, expected owner and actual application.

`EndpointIdentityNormalizer` produces invariant lowercase ASCII-alphanumeric keys, ignoring dash,
underscore, dot and whitespace. RabbitMQ, NATS, Kafka and in-memory topology all use this key. Moving
from raw resource names to canonical names is operator-managed: Lycia never auto-deletes old resources
or binds both forms.

### Transport mapping

| Semantic identity | RabbitMQ | NATS JetStream | Kafka |
|---|---|---|---|
| Command address | direct exchange `command.{Type}`; key `{Owner}` | `command.{Owner}.{Type}` | `{prefix}.command.{Owner}.{Type}` |
| Command consumer | `command.{Type}.{ApplicationId}` | durable consumer from the logical command queue | one consumer group from the logical command queue |
| Event address | fanout exchange `event.{Type}` | `event.{Type}` | `{prefix}.event.{Type}` |
| Event consumer | `event.{Type}.{Handler}.{ApplicationId}` | one durable consumer per handler/application | one group per handler/application |
| Response address | direct exchange `response.{Type}`; key `{ResponseEndpoint}` | `response.{ResponseEndpoint}.{Type}` | `{prefix}.response.{ResponseEndpoint}.{Type}` |
| Response consumer | `response.{Type}.{ApplicationId}` | requester durable consumer | requester consumer group |

RabbitMQ queues are durable, non-exclusive and non-auto-delete, each with a `.dlq` dead-letter queue.
Event routing keys are empty because a per-type fanout exchange distributes. Command queue identity
excludes the handler class, so renaming a handler does not create a new queue. RabbitMQ.Client automatic
recovery redeclares topology after a broker restart.

JetStream is the NATS default, with explicit acknowledgements, bounded redelivery and durable consumers.
Core NATS is for intentionally ephemeral workloads and cannot retain a command while subscribers are
absent.

Kafka commits an offset only after listener acknowledgement. Ordering is partition-scoped with key
preference `CorrelationId`, then `SagaId`, then `MessageId`; replicas above the partition count stay idle.
Kafka transactions do not remove the need for idempotent handlers.

Lycia ownership is not broker-global exclusivity: an independently bound queue, group or durable can
consume the same logical stream. `Send(command, handlerType, ...)` overloads remain source compatible,
but `handlerType` is correlation and tracing context and does not affect command routing.
`MessagingNamingHelper.GetRoutingKey` and the topic-style helpers are obsolete compatibility APIs.

---

## SagaStore providers

Providers are selected explicitly with `UsePersistence().With...SagaStore(...)`. A second selection
throws at configuration time (`LyciaPersistenceBuilder.SelectProvider`).

- **InMemory** — `InMemorySagaStore`, composite key of `SagaId`, `MessageId` and `HandlerType`, stored in
  process memory.
- **Redis** — `RedisSagaStore`, with atomic Lua compare-and-set operations. Step metadata keys use
  `step:{StepName}:handler:{HandlerName}:message-id:{MessageId}`.
- **SQL Server / PostgreSQL** — embedded migrations (`dbo.LyciaSagaData`/`dbo.LyciaSagaSteps`,
  `lycia_saga_data`/`lycia_saga_steps`), explicit `BIGINT` version columns and parameterized ADO.NET (no
  `MERGE`, no `rowversion`/`xmin` as the public version). `LoadSagaDataAsync` is a pure read; the first
  version is written by the first explicit save, so it is journaled like every other transition.

All four implement `IVersionedSagaStore`:

```csharp
long newVersion = await versionedStore.SaveSagaDataAsync(sagaId, data, expectedVersion: currentVersion);
```

A mismatched `expectedVersion` throws `SagaConcurrencyException`. Relational providers use
`UPDATE ... SET Version = Version + 1 WHERE SagaId = @id AND Version = @expected` with a rows-affected
check; Redis uses a single Lua `EVAL`.

Step statuses are `None`, `Started`, `Completed`, `Failed`, `Compensated`, `CompensationFailed` and
`Cancelled`; transitions are validated, and invalid ones (such as compensating an already-compensated
step) are rejected.

---

## Inbox

`IInboxStore` (`Lycia.Saga.Abstractions.Inbox`) records the processing identity of incoming messages,
keyed by `(MessageId, HandlerType)`. It gates before the handler body runs; the SagaStore step log stays
the source of truth for saga progress and compensation. `IInboxStore` is resolved optionally, and with
none registered dispatch skips the Inbox entirely.

`TryBeginAsync` returns `Started`, `AlreadyProcessing`, `AlreadyCompleted` or `AlreadyFailed`.
`AlreadyFailed` is logged as a warning, since that delivery is being dropped while the earlier failure is
inside its suppression window.

**Claim recovery.** A record in `Processing` (the process died after committing its claim) or `Failed`
becomes claimable again once older than `ClaimRecoveryTimeout` (default 5 minutes, on `InboxOptions`,
`SqlServerInboxOptions` and `PostgreSqlInboxOptions`; the InMemory store uses the default). Takeover is
atomic in every provider:

- SQL Server / PostgreSQL — one predicated `UPDATE` that only succeeds while the row is still stale.
- Redis — a Lua script that checks status and age and rewrites the record in one step.
- InMemory — the store's lock.

`Completed` is never reclaimable. Without the recovery window the first crash or handler failure would
make every later redelivery a permanent no-op: the dispatcher returns normally, the transport acks, and
the work is dropped. Configure the window longer than the slowest handler.

Under `LocalAtomic` the claim is part of the handler transaction and rolls back on any failure, so the
window matters only where the claim commits independently (Redis, InMemory, or `Independent`
relational topologies).

**Providers.** Redis uses per-`(HandlerType, MessageId)` keys. SQL Server (`LyciaInbox`) and PostgreSQL
(`lycia_inbox`, `jsonb` columns) create their tables through `002_InboxOutboxSchema.sql`, applied only
when an Inbox or Outbox is enabled.

---

## Outbox

### Capture

`IOutgoingMessagePipeline` is the single direct-versus-durable selection point, used by every saga
context (including tracked adapters) and by due-schedule dispatch. `DirectOutgoingMessagePipeline`
delegates to `IEventBus`. `OutboxOutgoingMessagePipeline` serializes a versioned `OutboxEnvelope`: stable
identity, operation (`Send`, `Publish` or `Respond`), body and headers (including the captured trace
context), handler/application/saga routing, and for responses the original request needed for targeted
routing. Scheduling is deliberately not an Outbox operation: the schedule store owns the intent until it
is due, then hands the original operation to this pipeline.

`IOutboxStore.AddAsync` is idempotent on `MessageId`.

### Worker and dispatcher

`OutboxWorker` is registered by every `.With...Outbox()` provider and configured with
`.WithOutboxWorker(...)` (`BatchSize`, `MaxAttempts`, `RecoveryTimeout`, `PollInterval`, `RetryBackoff`,
`MaxRetryBackoff`, `MaxJitter`). Each pass creates a scope, claims a batch and dispatches it, honouring
shutdown cancellation. After a pass that ends with unconfirmed, failed or abandoned messages, or throws,
the worker backs off exponentially with jitter; otherwise it polls every `PollInterval`.

`ClaimPendingBatchAsync` returns `Pending` rows, plus `ConfirmationUnknown`, `Claimed` and `Publishing`
rows whose last update is older than `RecoveryTimeout`. The attempt cap gates only the statuses a *new*
attempt starts from: a `Pending` or `ConfirmationUnknown` row at `MaxAttempts` is never returned, but a
stale `Claimed`/`Publishing` row is returned whatever its `RetryCount` (see
[Crash recovery](#crash-recovery-of-an-in-doubt-attempt)). Claiming is atomic per provider:

- SQL Server — `UPDATE TOP (@n) ... OUTPUT INSERTED.*` with `READPAST`.
- PostgreSQL — `FOR UPDATE SKIP LOCKED`.
- Redis — a Lua script that pops from the pending sorted set and updates status in one `EVAL`.
- InMemory — the store's lock.

Concurrent replicas rely on this claim atomicity; the Outbox has no scheduler-style fencing token
because claim ownership is a short lifecycle.

`OutboxDispatcher` restores the original operation and restores the captured trace context as the
parent of an `Outbox.{Operation}` producer span, so transport header injection continues the original
trace.

### Confirmation contract

Only a confirmed transport's success becomes `Published`. A transport confirms when it implements
`IConfirmedEventBus` and, if it also implements `IConditionalConfirmedEventBus`, reports
`ConfirmationsAvailable`. The dispatcher chooses the confirmed methods on that basis, not on the interface
alone:

| Transport | Confirms | Why |
| --- | --- | --- |
| Kafka | Always | Idempotent producer with `Acks.All`; the delivery report is the confirmation |
| NATS JetStream | Yes | The JetStream publish acknowledgement (`ack.EnsureSuccess()`) |
| Core NATS | No | Core NATS has no publish acknowledgement; `ConfirmationsAvailable` is false |
| RabbitMQ | While `PublisherConfirms` is enabled (default) | RabbitMQ's publisher confirm; see [RabbitMQ publisher confirms](#rabbitmq-publisher-confirms) |

An unconfirming transport is called through plain `Send`/`Publish`/`Respond`, and a completed attempt is
recorded as `ConfirmationUnknown`. A transport exception is also `ConfirmationUnknown`, because the broker
may have received the message before the failure. Only a permanent local error before any publish —
envelope, type resolution or serialization — becomes `Failed`.

This is deliberately conservative: a message is never recorded as delivered without a positive
confirmation, and the cost is redelivery, which the at-least-once contract and the Inbox already absorb.
`IConditionalConfirmedEventBus` exists because a type check cannot express "confirms, but only in this
configuration". Before it, `NatsEventBus` implemented `IConfirmedEventBus` unconditionally although its
confirmed methods throw in Core mode, so an Outbox on Core NATS would have failed every attempt.

### RabbitMQ publisher confirms

**What is confirmed.** RabbitMQ sends `basic.ack` once it has taken responsibility for a published message:
for a routable message, when every queue it routes to has accepted it — for a persistent message on a
durable queue that means persisted to disk (classic queues may batch the write for up to a few hundred
milliseconds), and for a quorum queue that a quorum of replicas accepted it. An unroutable message is acked
once the exchange determines that no queue receives it, after a `basic.return` if it was published as
mandatory. `basic.nack` only happens when a queue's Erlang process fails, for example a queue with
`x-overflow=reject-publish` that is full. A confirm covers publisher-to-broker communication only and is
unrelated to consumer acknowledgements. Lycia declares durable queues and publishes persistent messages
(`Persistent = true`); it does not set a queue type, so the broker's default (classic) applies.

**Channels.** `RabbitMqEventBus` keeps two channels. `_channel` declares topology and hosts consumers.
`_publishChannel` is created with confirms enabled and serves every publish: `Send`, `Publish`, `Respond`,
dead-lettering (`PublishToDeadLetterQueueAsync`) and native scheduling (`ScheduleNativeAsync`). Publishing
is serialized (`_publishLock`): the client documents that a channel must not be shared by concurrent
publishers, and the confirmation bookkeeping assumes one publish in flight. The channel is created in one
place (`CreatePublishChannelAsync` / `CreatePublishChannel`) so a recreated channel cannot come back without
confirms, and the client's automatic recovery restores them on a recovered one (verified against a broker
restart on both client generations). Exchange declarations are cached per publish channel instance; a
channel discarded after a failure starts with an empty cache, which heals a deleted exchange.

**Client APIs.** The 7.1.2 build (net8.0+) uses `IConnection.CreateChannelAsync(new
CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true))`;
with tracking enabled `IChannel.BasicPublishAsync` completes only when the broker confirms, and a nack or a
returned mandatory message surfaces as `PublishException` (`IsReturn` distinguishes the two). The 6.8.1
build (netstandard2.0) uses `IModel.ConfirmSelect()` and the `BasicAcks`, `BasicNacks` and `BasicReturn`
events. Neither client generation needed a dependency change.

**The 6.8.1 `WaitForConfirms` defect.** `IModel.WaitForConfirms` answers from a shared `_onlyAcksReceived`
flag that the ack/nack handler updates on another thread, and the signal that unblocks the waiter is not
atomic with that update. Under rapid publish-then-publish it can report a broker nack as an ack, which would
record a rejected message as delivered. This was measured, not assumed: 40 rapid publishes to a queue that
holds five and rejects the rest produced six "confirmed" instead of five in every run. The netstandard2.0
path therefore does not call `WaitForConfirms`. `PublishConfirmTracker` arms the publish sequence number
(`NextPublishSeqNo`) before sending and resolves it from the ack/nack frames itself, and
`Every_rejected_publish_is_reported_as_rejected_under_rapid_publishing` pins the exact counts on both client
generations.

**Routing policy.** With confirms on, commands and responses are published `mandatory`: each has exactly
one owner queue, so no route is a failure, and through the Outbox a command whose owner has not declared its
queue yet is retried instead of silently lost. Events are only mandatory when
`EventBusOptions.RequireRoutableEvents` is set, because an event may legitimately have no subscriber (the
Microservices sample publishes one nobody consumes). With confirms off the historical non-mandatory
fire-and-forget behavior is kept.

**Outcomes and Outbox mapping.**

| Broker/client outcome | Exception | Outbox result |
| --- | --- | --- |
| `basic.ack` | none | `Published` |
| `basic.nack` | `RabbitMqPublishNackedException` | failed attempt (retried; final attempt `Abandoned`) |
| `basic.return` (mandatory, unroutable) | `RabbitMqUnroutableMessageException` | failed attempt (retried until a route exists; final attempt `Abandoned`) |
| No confirm within `PublisherConfirmTimeout`; channel or connection lost while waiting | `RabbitMqPublishOutcomeUnknownException` | failed attempt; the same `MessageId` is republished |
| Connection cannot be established, or the exchange declaration fails or times out | the underlying exception (`TimeoutException` for a declaration that did not answer) | failed attempt; nothing was published |
| Caller cancellation | `OperationCanceledException` | never `Published`; the dispatcher's shutdown handling applies |

An unknown outcome is deliberately not converted into a definite failure: the broker may already hold the
message. After an unknown outcome, a timeout or cancellation the publish channel is discarded, so an
outstanding confirmation from the abandoned publish cannot be mistaken for the next one.

**Crash windows.** Publisher confirms narrow the ambiguity window; they do not close it, and Lycia remains
at-least-once.

| Window | Result |
| --- | --- |
| Crash before `BasicPublishAsync` | Row is `Publishing`; recovered by the [crash-recovery](#crash-recovery-of-an-in-doubt-attempt) rules |
| Crash during `BasicPublishAsync` | Same. The broker may or may not have the message |
| Broker holds the message, connection dies before the client observes the confirm | `RabbitMqPublishOutcomeUnknownException`; republished with the same `MessageId`. Two copies exist; the Inbox absorbs the duplicate (tested with a fault-injecting proxy) |
| Client observes the confirm, process dies before the Outbox records `Published` | Row is `Publishing`; recovered and republished (a duplicate) |
| Broker restart | The client recovers connection and channels; confirms remain enforced; the Outbox retries in the meantime |
| Two recovering workers | Exactly one takes a stale row through the store's atomic claim |

### Retry and exhaustion

`ConfirmationUnknown` is gated by `RecoveryTimeout`, like a claim stranded by a crashed worker, so
attempts are spread across roughly `MaxAttempts × RecoveryTimeout`. Without that gate an unconfirming
transport republishes the same message `MaxAttempts` times in a burst and a short broker outage burns
the whole attempt budget.

When the last permitted attempt (`RetryCount + 1 >= MaxAttempts` as it starts) ends:

- **never reached the transport** (the publish threw), the dispatcher calls
  `MarkAbandonedAsync`, records the reason as failure info, logs a warning naming `MessageId` and
  `SagaId`, and counts it in `OutboxDispatchResult.Abandoned`, which `OutboxWorker` also logs. Without
  this terminal state, a row that exhausted its attempts against an unreachable broker would sit at the
  attempt cap with no terminal status and no recorded reason.
- **was accepted but not confirmed**, the row stays `ConfirmationUnknown` at the cap and the dispatcher
  logs at Information. An unconfirming transport reports every successful publish this way, so
  abandoning it would flag essentially every delivered message.

`Abandoned` is neither `Published` nor `Failed`. Implementations of `MarkAbandonedAsync` must record the
failure info and remove the message from any claimable queue (Redis removes it from the pending set).

### Crash recovery of an in-doubt attempt

**What `RetryCount` means.** It is the number of attempts *started*: `MarkPublishingAsync` increments it
before the transport is called, and nothing else does. It therefore includes an attempt whose outcome was
never recorded because its worker stopped, and it is never reset. It can reach `MaxAttempts + 1`.

**Why the cap alone cannot decide recovery.** The cap answers "may another attempt start?". A stale
`Claimed`/`Publishing` row poses a different question: an attempt already started whose outcome nobody
recorded. Only a live worker can resolve it, and a worker can only find it through the claim query. If the
claim query also applied the cap to such rows, a worker dying on the *final* attempt — after
`MarkPublishingAsync` had raised the count to the cap — would leave a row that no claim ever returns:
neither retried nor terminal, silently lost. (Redis was worse: its script popped the due entry off the
pending set before checking the cap and never re-added it.) So the claim contract is:

| Row | Handed back when |
| --- | --- |
| `Pending` | `RetryCount < maxAttempts` |
| `ConfirmationUnknown` | `RetryCount < maxAttempts` and older than `RecoveryTimeout` |
| `Claimed`, `Publishing` | older than `RecoveryTimeout`, **whatever** `RetryCount` |
| `Published`, `Failed`, `Abandoned` | never |

**What the dispatcher does with a handed-back row**, from `RetryCount` (`n`) at claim time:

| `n` | Meaning | Action |
| --- | --- | --- |
| `< maxAttempts` | An ordinary attempt, or a stale one below the cap | Attempt `n + 1`, as usual |
| `== maxAttempts` | The last permitted attempt is in doubt | One recovery attempt (`n` becomes `maxAttempts + 1`) with the same `MessageId`, logged as a warning |
| `> maxAttempts` | The recovery attempt was lost too | `Abandoned` with a recorded reason; nothing is published |

The recovery attempt is treated as the final attempt: if its publish throws, the row becomes `Abandoned`;
if the transport accepts it without confirming, the row stays `ConfirmationUnknown`; a confirming
transport gives `Published`.

**Why an indeterminate attempt is republished.** Recovery cannot tell whether the crashed attempt died
before the broker accepted the message or after, and the two need opposite answers: silently dropping the
row loses a message that never left, while assuming delivery or abandoning it on the strength of the count
alone discards intent that may still be undelivered. Lycia is at-least-once and consumers already absorb
duplicates through the Inbox, so the conservative answer is to publish once more. A duplicate is an
accepted cost; a lost message is not. `MessageId` is unchanged, so the duplicate is recognizable.

**Why it is bounded.** Crash recovery adds exactly one attempt beyond `MaxAttempts`, and a message that
keeps killing its worker is abandoned after that rather than looped. It is also kept separate from normal
retry policy: below the cap a stale row is an ordinary retry with no special handling, and the cap on
`ConfirmationUnknown` rows is unchanged.

**Shutdown.** Cancelling the last permitted attempt on shutdown writes no outcome and leaves the row
`Publishing`, the state a crash leaves, so the next worker recovers it. A graceful stop is therefore never
worse than a crash by demanding operator action. Earlier attempts still record `ConfirmationUnknown`.

**Contract for custom stores.** `ClaimPendingBatchAsync` must hand back stale `Claimed`/`Publishing` rows
without applying the cap, must claim them atomically so two workers never own the same stale row, and must
return `RetryCount` unmodified. A store that keeps applying the cap to them reintroduces the stranded-row
window.

**Other states that rest.** A `ConfirmationUnknown` row at the cap stays put by design: the transport
accepted the last attempt but cannot confirm it. The row remains queryable by status; it is not flagged
`Abandoned` because nothing indicates the message was undelivered.

---

## Atomic persistence session

`ILyciaPersistenceSession`/`ILyciaPersistenceSessionFactory` (`Lycia.Saga.Abstractions.Persistence`) are
the provider-neutral, service-local transaction boundary. `RelationalPersistenceSession` wraps a real
`DbConnection`/`DbTransaction` and is registered by the SQL Server and PostgreSQL SagaStore providers
(`SupportsAtomicTransactions = true`). A scoped `ILyciaPersistenceSessionAccessor` gives relational stores
explicit access to the dispatcher-owned session; it is neither static nor `AsyncLocal`.

`Auto` is the default policy. Stores whose normalized database identities match resolve `LocalAtomic`;
mixed providers or different databases resolve `Independent`. `.RequireAtomicBoundary()` and
`.UseIndependentTransactions()` are the only overrides — there is no `WithAutomaticConsistency()`,
`WithStrongConsistency()` or `AllowNonAtomicBoundary()`. The boundary never spans services and never
includes application business tables.

**Unknown commit outcome.** If a COMMIT is issued but its result cannot be observed, the session throws
`PersistenceCommitOutcomeUnknownException`. `SagaDispatcher` does not catch or reclassify it: the Inbox
is not marked `Failed` and the handler is not re-run. The durable identities are the recovery authority —
the Inbox claim shows whether the handler completed, `SagaData.Version` whether the state write landed,
and the journal `TransitionId` and Outbox `MessageId` make their own writes idempotent.
`SqlServerFailureWindowTests`/`PostgreSqlFailureWindowTests` simulate connection loss before and during
commit and assert this path.

---

## Split Store and reconciliation

The Split Store request path commits relational state plus a `SagaProjectionIntent`; it never
dual-writes Redis. `SplitStoreSagaStore.SaveSagaDataAsync` writes the canonical state, the reconciliation
intent and the journal entry in one call, all enlisted in the `LocalAtomic` session.

`ReconciliationWorker` (tuned with `WithReconciliationWorker(...)`: `BatchSize`, `MaxAttempts`,
`PollInterval`, `RetryBackoff`, `MaxRetryBackoff`, `MaxJitter`, `ClaimTimeout`) claims intents with
provider-native claims and installs complete target states through `IOperationalSagaProjectionStore`,
whose Redis implementation uses compare-and-set on the saga version. Version, not time, orders work:
equal versions are idempotent, and an older intent is marked `Superseded`. Intent statuses are
`Pending`, `Claimed`, `Applied`, `RetryPending`, `Failed` and `Superseded`; an intent that exhausts
`MaxAttempts` becomes `Failed` with failure code `AttemptsExhausted`.

`ISagaProjectionReconciler.RestoreLatestAsync` requeues the current canonical materialization. It
restores from the latest canonical row, not ordered history; journal rebuild (below) is the ordered,
verifiable path.

Relational state is canonical and Redis is rebuildable. Reconciliation never turns Redis into request-path
authority: handler reads are canonical, so Outbox dispatch does not depend on reconciliation.

---

## Canonical journal and deterministic rebuild

`UseSplitStore()` requires `ISagaJournalStore`, `IReconciliationStore` and
`IOperationalSagaProjectionStore`. The journal is a separate table from reconciliation intents: intents
are claimed, completed and superseded away, so they cannot serve as retained ordered history.

**Contracts** (`Lycia.Saga.Abstractions.Persistence.Journal`):

- `SagaJournalEntry` — one transition: identity (`JournalEntryId`/`TransitionId`), ordering
  (`SagaId` + `SequenceNumber`, always equal to `TargetVersion`), best-effort correlation metadata
  (`MessageId`, `RequestId`, `CorrelationId`, `CausationId`, `ParentMessageId`, `ApplicationId`,
  `HandlerType`, `MessageType`), schema versions (`MessageSchemaVersion`, `JournalSchemaVersion`),
  `TransitionType` (`Created`/`Updated`/`Completed`/`Failed`), and the payload: the full post-transition
  `SagaDataPayload` plus a full `StepsSnapshotPayload` of the step log. `CreatedAtUtc` is diagnostic only
  and never used for ordering.
- `ISagaJournalStore` — `AppendAsync` (idempotent on `TransitionId`), `ReadAsync(sagaId, afterVersion,
  maxCount)` (ordered and bounded), `GetLatestVersionAsync`, and cursor-based
  `EnumerateSagaIdsAsync(afterSagaId, maxCount)`. Relational implementations enlist through
  `RelationalConnectionLease`/`ILyciaPersistenceSessionAccessor`; no `DbConnection` appears on the public
  surface.
- `ISagaJournalReducer` — pure `Reduce(previous, entry)`. Because each entry is a full snapshot, the
  reducer is a direct projection rather than a fold.
- `ISagaJournalContextAccessor`/`SagaJournalTransitionContext` — framework-managed correlation metadata,
  set by `SagaDispatcher` before dispatch and cleared afterwards. Handler code never sets it.
- `IJournalEntryUpcaster`/`JournalEntryUpcastChain` — deterministic, chained schema upgrades before
  reduction. A missing upcaster fails rebuild or verify clearly.
- `ISagaRebuildService` — one engine for automatic and operator rebuild: `RebuildSagaAsync`/
  `RebuildAllAsync` (bounded pages, per-saga failure isolation, `IProgress<SagaRebuildProgress>`,
  cancellation, a resumable `ResumeCursor`) and non-mutating `VerifySagaAsync`/`VerifyAllAsync`
  (`Healthy`, `MissingProjection`, `VersionMismatch`, `StateMismatch`, `JournalGap`,
  `SchemaUnsupported`, `CorruptEntry`). `StateMismatch` compares the canonical SagaStore version via
  reflection when the stored SagaData type resolves; when it does not, `canonicalVersion` is `null` and
  verification does not fail on that.

**Continuity.** While replaying, each entry's `PreviousVersion` must equal the previously folded version
and `TargetVersion` must exceed it; gaps and backward transitions are `JournalGap`/`CorruptEntry`, never
best-effort reconstruction.

**Installation** goes through the same `IOperationalSagaProjectionStore.ApplyAsync` that reconciliation
uses, so a stale rebuild cannot overwrite a newer projection and repeating a rebuild is a no-op.

**Side-effect isolation.** `SagaRebuildService` depends only on the journal store, reducer, projection
store, canonical SagaStore and upcast chain; a test inspects its constructor to prove it cannot invoke
handlers, publish, or write the Inbox or Outbox.

**Schema.** `004_SagaJournal.sql` creates `dbo.LyciaSagaJournal` / `lycia_saga_journal` with the primary
key on `TransitionId` and `UNIQUE (SagaId, TargetVersion)` as a corruption guard (SagaStore optimistic
concurrency is what prevents two writers reaching the same version). It is applied only by the canonical
Split Store entry points (`With...CanonicalSagaStore`), so plain relational SagaStore deployments do not
grow a journal.

The journal is framework-level canonical transition history, not a business event-sourcing stream. A
snapshot table is not needed today because each entry is already a full snapshot.

---

## Transport-independent scheduling

Scheduling contracts live in `Lycia.Saga.Abstractions`; orchestration, the in-memory components and the
Redis schedule store live in `Lycia.Extensions.Scheduling`; the RabbitMQ TTL + DLX strategy lives in
`Lycia.Extensions.RabbitMq`. Kafka and the supported NATS baseline use the durable dispatch worker.

`LyciaSchedulingBuilder.WithDispatch(Action<SchedulerWorkerOptions>)` is the public entry point. It
configures `SchedulingOptions.Worker`, read by the internal hosted `SchedulerWorker`. `WithWorker(...)`
is an `[Obsolete]` wrapper over the same options.

Redis creation and claiming use scripts. A claim has an expiring lease and a monotonic fencing token;
active dispatches renew the lease, and every state mutation checks both owner and fence
(`EvaluateOwnedMutationAsync`, the same pattern as `RedisVacuumLeaseManager`), so a stale owner cannot
mutate a claim it no longer holds. Failed dispatch attempts back off exponentially with jitter
(`SchedulerWorkerOptions.MaxRetryBackoff`/`MaxJitter`). The stored payload keeps its original
`MessageId`, and responses keep the request payload so dispatch calls `Respond(request, response)` with
the original routing and identities. Completion is recorded after transport acceptance; a crash between
acceptance and completion can repeat a message.

RabbitMQ predefined scheduling uses fixed queue TTL plus DLX; dynamic delay queues are opt-in and
registered with exact provenance. Vacuum uses a per-transport lease and fence, active replica manifests,
schedule references, broker message/consumer facts, age and idle thresholds, and conditional deletion.
Ordinary topology follows a separate orphan/quarantine state machine and defaults to report-only;
inactivity or a matching name never proves ownership. The `Lycia.Scheduling` activity source and meter
and the `LyciaScheduling` health check never include payload data.

---

## Observability

`ActivityTracingMiddleware` reuses the listener's consumer activity (or starts `Saga.{Handler}`) and
tags it with `lycia.saga.id`, `lycia.message.id`, `lycia.correlation.id`, `lycia.causation_id`,
`lycia.parent_message_id`, `lycia.request_id`, `lycia.response_endpoint`, `lycia.handler`,
`lycia.application.id` and `lycia.saga.step.status` (snake_case duplicates such as `lycia.saga_id` are
also emitted). An exception escaping the pipeline sets the error status and `exception.*` tags; a failure
recorded by a handler base class is reported by the compensation coordinator as described in
[Handler failure visibility](#handler-failure-visibility). RabbitMQ consumer spans also carry
`messaging.system`, `messaging.destination` and `messaging.operation`.

`LyciaOpenTelemetryExtensions.AddLyciaTracing()` adds the `Lycia` activity source and sets the W3C trace
context and baggage propagators as the default text-map propagator. `LyciaTracePropagation` injects and
extracts through that propagator, so without `AddLyciaTracing()` every message hop would start a
disconnected trace.

`ILyciaReliabilityDiagnostics.GetSnapshot()` (`Lycia.Extensions.Reliability`) composes a
`LyciaReliabilitySnapshot` from existing signals: `IPersistenceTopology.Current` (resolved optionally)
plus registration checks for `ISagaJournalStore`, `ISagaRebuildService`, `IInboxStore` and
`IOutboxStore`. It keeps no second copy of topology state and is registered unconditionally by
`AddLycia`.

Registration presence is checked with `IServiceProviderIsService.IsService(typeof(T))`, never
`GetService<T>() != null`. Several store factories (for example the Redis Inbox/Outbox registrations)
resolve `IConnectionMultiplexer`, whose own registration calls `ConnectionMultiplexer.Connect(...)`
synchronously the first time it is resolved - `GetService<T>()` would have turned a "is this configured?"
read into a real connection attempt, which is exactly what this abstraction exists to avoid. Any future
capability check added to `GetSnapshot()` must use the same `IsService` pattern.

### Diagnostics endpoint

`Lycia.Extensions.AspNetCore` (`MapLyciaDiagnostics()`) is a thin HTTP projection on top of the flow
above, and adds nothing to it:

```
configuration -> topology resolution (PersistenceTopologyResolver)
              -> IPersistenceTopology.Current
              -> ILyciaReliabilityDiagnostics.GetSnapshot() -> LyciaReliabilitySnapshot
                     |                    |
                     v                    v
              startup logging /    LyciaDiagnosticsResponse.FromSnapshot(...)
              custom tooling              |
                                           v
                                  GET /diagnostics/lycia
```

`LyciaDiagnosticsEndpointExtensions.MapLyciaDiagnostics` resolves `ILyciaReliabilityDiagnostics` from
`HttpContext.RequestServices` per request (it is registered `Scoped`) and calls only `GetSnapshot()`. It
never touches `IServiceCollection`, never resolves a store instance, and never re-derives `Mode`,
`ResolvedStrategy`, `CanonicalStore`/`OperationalStore` or the capability flags - doing so would create a
second topology-inference engine that could drift from `ILyciaReliabilityDiagnostics`. Extending what the
endpoint reports always means extending `LyciaReliabilitySnapshot` (and, through it, `GetSnapshot()`)
first, never adding logic to the endpoint itself.

**Diagnostics vs. health checks.** This endpoint answers "how is Lycia configured and what topology did
it resolve?", never "is Redis/RabbitMQ/PostgreSQL/SQL Server/Kafka/NATS reachable right now?". It performs
no network calls and normally returns `200 OK`, independent of whether the described infrastructure is up.
A liveness/readiness probe is a different, unrelated concern the application wires up on its own (for
example `Microsoft.Extensions.Diagnostics.HealthChecks`, already a transitive dependency of
`Lycia.Extensions`) - Lycia does not conflate the two, and `MapLyciaDiagnostics()` must never grow a
dependency-reachability code path.

**Secret-free boundary.** `LyciaDiagnosticsResponse` (`Lycia.Extensions.AspNetCore`) is a deliberate,
closed DTO: `DeliveryGuarantee`, `Persistence` (`Mode`, `Provider`, `ResolvedStrategy`, `CanonicalStore`,
`OperationalStore`) and `Capabilities` (`Inbox`, `Outbox`, `Journal`, `JournalRebuild`, `Reconciliation`).
It is built only from `LyciaReliabilitySnapshot`, which is itself already secret-free by construction -
notably it carries `PersistenceStoreDescriptor.ProviderName` but never `ConnectionIdentity` (the
normalized `host/database` string used internally for `LocalAtomic`/Split Store matching), so a host name
never reaches the wire. Nothing in the DTO is a message payload, `SagaData`, an Inbox/Outbox record, a
journal entry, or raw configuration. `LyciaDiagnosticsResponseTests`-equivalent coverage lives in
`tests/Lycia.Extensions.AspNetCore.Tests` (`SecretsTests`), which asserts unmistakable secret sentinel
values never appear in the serialized response from a fully wired application, not just from the DTO's
declared shape.

**Package placement.** `Lycia.Extensions.AspNetCore` is its own package (`net8.0;net9.0;net10.0`, plus
`FrameworkReference Microsoft.AspNetCore.App`) rather than a method added to `Lycia.Extensions`, because
Minimal API endpoint routing requires the ASP.NET Core shared framework, which does not exist for
`netstandard2.0`/net48 - every other Lycia package's floor - and a `FrameworkReference` would impose that
runtime dependency on every consumer, including plain worker/console services with no HTTP surface at
all. It depends only on `Lycia` and `Lycia.Extensions` (the same pattern every other add-on package uses),
never on a transport or persistence-provider package, and it is entirely opt-in: `AddLycia(...)` registers
`ILyciaReliabilityDiagnostics` unconditionally (see above) but never maps a route; only an explicit
`MapLyciaDiagnostics()` call does.

**Tests protecting the contract** (`tests/Lycia.Extensions.AspNetCore.Tests`, in-memory `TestServer`, no
external infrastructure): `MappingTests` (default/custom route, opt-in, unsupported verbs);
`ProjectionTests` (the response is exactly what a stub `ILyciaReliabilityDiagnostics` returns, across
Standard/LocalAtomic/Independent/Split Store and every provider name); `SecretsTests` (a real `AddLycia` +
`WithRedisSagaStore` wired with secret sentinel values never leak into the response); `CompositionTests`
(`RequireAuthorization(...)` on the returned builder is enforced by standard ASP.NET Core authentication/
authorization - Lycia implements none of its own); `NoProbeTests` (the request completes in milliseconds
against an unroutable Redis address, proving no connection is attempted on the request path). The same
eager-connection risk is also covered directly against `LyciaReliabilityDiagnostics.GetSnapshot()` in
`tests/Lycia.Tests/LyciaReliabilityDiagnosticsTests.cs`.

---

## Saga patterns

| Pattern | State | Handlers |
| --- | --- | --- |
| Choreography (reactive) | Stateless, no `TSagaData`; compensation through `ISagaCompensationHandler<T>` | `StartReactiveSagaHandler<TStart>`, `ReactiveSagaHandler<TMessage>` |
| Sequential orchestration (coordinated) | `TSagaData`; failures compensate through `Context.ContinueCompensation().ThenMarkAsCompensated<T>().ThenBubbleUp(ct)` (the only public entry point to parent propagation) | `StartCoordinatedSagaHandler<TStart, TSagaData>`, `CoordinatedSagaHandler<TMessage, TSagaData>` |
| Request-response orchestration | `TSagaData`; each step sends a command and continues on the response | `StartCoordinatedResponsiveSagaHandler<TStart, TResponse, TSagaData>`, `CoordinatedResponsiveSagaHandler<TMessage, TResponse, TSagaData>`, `IResponseSagaHandler<TResponse>` |

`Sample.Order.Orchestration.Consumer` and the Microservices sample use request-response orchestration.

Reactive handlers guard against duplicates with `Context.IsAlreadyCompleted<T>()`, governed by
`SagaOptions.DefaultIdempotency` (default `true`) and overridable per handler through
`EnforceIdempotency`.

---

## CancellationToken ownership in deferred/composite fluent APIs

Two APIs are deferred/composite: nothing they build executes until a terminal method is awaited, and one
`CancellationToken` governs the whole thing. Both follow the same rule: **the entry method never accepts
a token; only the terminal method does.**

- **Tracked messaging** (`SendWithTracking`/`PublishWithTracking`/`RespondWithTracking`/
  `ScheduleWithTracking` → `ISagaStepFluent.Then...`). `ReactiveSagaStepFluent`/`CoordinatedSagaStepFluent`
  (`Lycia.Saga`) close over an `Func<CancellationToken, Task> operation` built by the entry method (for
  example `ct => Send(nextCommand, ct)`) and run it from `RunAsync(CancellationToken, Func<CancellationToken, Task> transition)`:
  ```csharp
  private async Task RunAsync(CancellationToken cancellationToken, Func<CancellationToken, Task> transition)
  {
      cancellationToken.ThrowIfCancellationRequested();
      await operation(cancellationToken);
      await transition(cancellationToken);
  }
  ```
  A pre-cancelled terminal token throws before `operation` runs at all. There used to be a second,
  captured token accepted by the WithTracking call itself, resolved against the terminal token by
  `SagaStepFluentToken.Resolve(terminal, captured)` (terminal wins unless left `default`, in which case the
  captured token was used as a fallback). That type, and the captured-token constructor parameters on both
  fluent classes, are removed: there is now exactly one token per deferred tracked operation.
- **Compensation continuation** (`ContinueCompensation()` → `ICompensationContinuation`/`ICompensatedContinuation`,
  below) follows the identical shape, deliberately, for consistency.

This invariant does not extend to plain synchronous accessors or to APIs that were never deferred
(`Context.Send`/`Publish`/`Respond`/`Schedule`, `MarkAsComplete`, etc., which already took a token
directly and still do).

## Coordinated compensation continuation

### Staged fluent interfaces

```csharp
ICompensationContinuation ContinueCompensation();   // on ISagaContext<TInitialMessage>

public interface ICompensationContinuation
{
    Task ThenMarkAsCompensated<TStep>(CancellationToken cancellationToken) where TStep : IMessage;
    ICompensatedContinuation ThenMarkAsCompensated<TStep>() where TStep : IMessage;
}

public interface ICompensatedContinuation
{
    Task ThenBubbleUp(CancellationToken cancellationToken);
}
```

(`Lycia.Saga.Abstractions.Compensating`, implemented by `SagaCompensationContinuation`/
`SagaCompensatedContinuation` in `Lycia.Saga.Compensating`.) The two-parameter-list overload of
`ThenMarkAsCompensated<TStep>` is what makes `ThenBubbleUp` unreachable before it: the no-token overload
is the only one that returns `ICompensatedContinuation`, so the compiler only exposes `ThenBubbleUp` after
that specific call. `ContinueCompensation()` itself, and the no-token `ThenMarkAsCompensated<TStep>()`,
touch neither the SagaStore nor the compensation coordinator - `SagaCompensationContinuation` holds only a
context reference, and `SagaCompensatedContinuation` holds only a closed-over `Func<CancellationToken, Task>`.

- **Two-stage terminal**: `ThenMarkAsCompensated<TStep>(ct)` calls only `context.MarkAsCompensated<TStep>(ct)`
  - the same call `Context.MarkAsCompensated<TStep>(ct)` makes directly. No propagation. Use this for a
  root step, or for an intermediate step where you deliberately do not want to continue the chain.
- **Three-stage**: `ThenMarkAsCompensated<TStep>()` (no token) defers; `ThenBubbleUp(ct)` calls only the
  internal bubble-up primitive (below), which both marks the step compensated *and* durably requires -
  then immediately attempts - propagation to the logical parent. It does **not** also call
  `MarkAsCompensated` first; the two are one call into the coordinator, not two.

#### The staged-fluent encapsulation invariant

An intermediate stage of a staged fluent chain must never be an independently callable application
operation - only the type system's own shape should decide what is reachable, with a runtime check
reserved for the one case that genuinely cannot be expressed at compile time. Compensation is the clearest
example: the execution primitive behind `ThenBubbleUp` is `IBubbleUpCompensationPrimitive.BubbleUpCompensationAsync<TStep>`
(`Lycia.Saga.Abstractions.Compensating`) - an **internal** interface, not a member of `ISagaContext<TInitialMessage>`.
Every built-in saga context (`SagaContext<TInitialMessage>` and its `TSagaData` override, both
`StepSpecificSagaContextAdapter` variants) implements it as an *explicit* interface implementation, so it
is invisible even on the concrete, public `SagaContext<T>` type - `Context.BubbleUpCompensationAsync<T>(ct)`
does not compile, and never did as a matter of the type system rather than convention.
`SagaCompensationContinuation` (the only intended caller, in the same assembly via `InternalsVisibleTo`)
reaches it with `context is IBubbleUpCompensationPrimitive primitive`; a custom `ISagaContext<T>`
implementation that doesn't implement it gets a clear `InvalidOperationException` from `ThenMarkAsCompensated<TStep>()`
instead of continuing silently or throwing an opaque `InvalidCastException` - this is the one place the
invariant is a runtime check, because "does this arbitrary external context support bubble-up" cannot be
expressed in the public type system without exposing the primitive itself. `SagaContext<TInitialMessage>`'s
own core implementation is `protected virtual BubbleUpCompensationCoreAsync<TStep>`, so
`SagaContext<TInitialMessage, TSagaData>` can still override the behavior without either level exposing a
public or even internally-callable-by-name method. `FluentApiEncapsulationTests.cs` (`Lycia.Tests`) proves
the absence by reflecting over the actual compiled public surface, not by code review.

The same shape applies to `SendWithTracking`/`PublishWithTracking`/`RespondWithTracking`/`ScheduleWithTracking`:
the entry method returns only `ISagaStepFluent`, a flat interface of terminal `Then...` methods that all
return `Task` - there is no member that returns another fluent/continuation type, and no generic `Execute()`
escape hatch. The deferred operation and transition delegates the entry method closes over are run by a
`private` `RunAsync` on the concrete `ReactiveSagaStepFluent`/`CoordinatedSagaStepFluent`, never public.

### `SagaCompensationCoordinator.CompensateParentAsync` - the durable model

The former single in-process call (mark compensated, then synchronously invoke the parent) is now two
independently durable facts, matching the reliability invariant this section exists to explain: **persisting
"current step Compensated" is never treated as proof that parent propagation completed, started, or is even
required.** `CompensateParentAsync` now does:

1. Reads the current step's recorded status. If it is already `CompensationFailed` - a genuine terminal
   business failure, not a retry of a success - it returns immediately: a failed compensation must never be
   silently overwritten as `Compensated` and must never propagate as if it had succeeded. This is the only
   status check left; unlike the old guard, it does **not** also block a step already `Compensated` - that
   was the exact mechanism behind the crash window this design closes (see "Root cause" below).
2. Logs the current step `Compensated`. Re-logging the same status for the same message is a safe,
   idempotent no-op via the store's own step-transition validation (`SagaStepHelper.ValidateSagaStepTransition`)
   - no separate guard is needed here either.
3. If `message.ParentMessageId == Guid.Empty` (a root step), returns. **No propagation intent is created for
   a root step** - see "Root behavior" below.
4. Otherwise, calls `ISagaStore.EnsureAndClaimCompensationPropagationAsync(sagaId, childMessageId,
   parentMessageId, owner, leaseDuration, maxAttempts, ct)`. This is the durable handoff: it durably
   *requires* propagation for this exact edge (`SagaId` + `ChildMessageId` - idempotent identity, so a
   redelivered call never creates a second logical intent for the same edge) and, in the same call, attempts
   to *claim* it for the immediate in-process attempt that follows. See "Propagation state machine" below
   for the claim outcomes.
5. Only on a `Claimed` outcome does it call `AttemptPropagationAsync` (below) to actually invoke the parent's
   compensation handler, right now, in-process. Every other outcome (`AlreadyCompleted`, `ClaimedByAnother`,
   `AttemptsExhausted`) means there is nothing more to do on this call - the durable state already reflects
   the correct next action (nothing, or "another owner/worker is already on it").

`AttemptPropagationAsync(sagaId, childMessageId, eventBus, sagaStore, ct)` is the shared implementation
behind both the immediate in-request attempt above and `CompensationWorker`'s recovery passes (below) -
resolving the logical parent from the current step snapshot (skipping one orchestrator response hop where
applicable, exactly as before), deserializing its original message, resolving its compensation handler, and
invoking it. A structurally unresolvable edge (missing parent step record, unresolvable type, undeserializable
payload) is marked `Failed` immediately via `MarkCompensationPropagationFailedAsync` - retrying cannot fix a
lineage/serialization problem, and silently returning would hide a real defect from operators. A handler
invocation that throws is left alone: the edge stays `Claimed`, and becomes reclaimable once its lease goes
stale - the bounded-retry path. On success, `MarkCompensationPropagationCompletedAsync` records completion.
It is intentionally `internal`, not part of `ISagaCompensationCoordinator` - both callers live in the same
assembly (`Lycia`), and this keeps storage-adjacent propagation mechanics off the public coordinator
interface (tests reach it via `InternalsVisibleTo`).

### Propagation state machine

`CompensationPropagationIntent` (`Lycia.Common.SagaSteps`) is a small, deliberately minimal durable record -
it does **not** duplicate the child's message payload or type (both are already durable in the step log's
`SagaStepMetadata`); it only tracks the propagation edge's own lifecycle:

```csharp
public enum CompensationPropagationStatus { Pending, Claimed, Completed, Failed }

public sealed class CompensationPropagationIntent
{
    public Guid SagaId { get; set; }
    public Guid ChildMessageId { get; set; }       // identity is SagaId + ChildMessageId
    public Guid ParentMessageId { get; set; }       // denormalized, for cheap operator visibility only
    public CompensationPropagationStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public string? Owner { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public SagaStepFailureInfo? FailureInfo { get; set; }
}
```

Four states, no decorative ones: `Pending` (durably required, unowned) → `Claimed` (owned by an in-request
attempt or a `CompensationWorker`, for up to its lease/recovery window) → `Completed` or `Failed` (both
terminal). `CompensationPropagationClaimOutcome` (`Claimed`, `AlreadyCompleted`, `ClaimedByAnother`,
`AttemptsExhausted`) is what `EnsureAndClaimCompensationPropagationAsync` returns for a single edge.

### Persistence authority: `ISagaStore`, not a second store

Five methods live directly on `ISagaStore` (not a separate, optional interface):
`EnsureAndClaimCompensationPropagationAsync`, `ClaimDueCompensationPropagationsAsync`,
`MarkCompensationPropagationCompletedAsync`, `MarkCompensationPropagationFailedAsync`,
`GetCompensationPropagationIntentAsync`. This is deliberate: compensation propagation durability is part of
SagaStore correctness, not an optional add-on a provider or an application can forget to configure - the
same reasoning that put step logging and saga-data persistence on `ISagaStore` in the first place. Every
built-in provider implements all five:

- **InMemory** (`Lycia.Stores.InMemorySagaStore`): a `ConcurrentDictionary<(SagaId, ChildMessageId),
  CompensationPropagationIntent>` guarded by one `_propagationLock` (mirrors the existing `_sagaVersions`
  lock's role) - `TryClaimLocked` is the single mutual-exclusion point both `EnsureAndClaim` and
  `ClaimDue` share, parameterized by the staleness threshold to apply (see the note in its own doc comment:
  the single-edge path uses its own `leaseDuration` as that threshold; the batch path uses `recoveryTimeout`
  - passing the wrong one for the batch path was a real bug this design's own conformance tests caught and
  fixed).
- **SQL Server / PostgreSQL** (`SqlServerSagaStore`, `PostgreSqlSagaStore`): a dedicated
  `LyciaCompensationPropagation` / `lycia_compensation_propagation` table (schema `005_CompensationPropagation`,
  applied unconditionally alongside the core `001` schema - not opt-in like Inbox/Outbox's `002`), with
  `PRIMARY KEY (SagaId, ChildMessageId)` giving the idempotent-identity guarantee at the database level, plus
  an index on `(Status, UpdatedAtUtc)` for the batch claim scan. The batch claim is the same
  "queue dequeue" pattern already used for Outbox: SQL Server `UPDATE ... WITH (ROWLOCK, READPAST) OUTPUT
  INSERTED.* FROM (SELECT TOP ...)`; PostgreSQL `UPDATE ... WHERE (...) IN (SELECT ... FOR UPDATE SKIP LOCKED
  LIMIT n) RETURNING ...`. No fencing token - the same time-window stale-claim-reclaiming plus atomic
  conditional write Outbox uses, which is why `CompensationWorker` follows Outbox's pattern rather than
  Scheduling's fencing-token model (see "Why not Outbox/Reconciliation" below for the reasoning, reused
  here). Both providers use `RelationalConnectionLease<TConnection,TTransaction>.OpenAsync(...)` exactly as
  `LogStepAsync`/`SaveSagaDataAsync` do, which is what gives this atomicity with other same-request writes
  *for free* when a LocalAtomic session is ambient (see "Transaction boundary" below) - no new transaction
  plumbing was invented.
- **Redis** (`RedisSagaStore`): a JSON blob per edge at `saga:compensation:{sagaId}:{childMessageId}`, plus a
  single `saga:compensation:pending` ZSET scored by each edge's due time (its member is
  `"{sagaId}|{childMessageId}"` - note the different separator from the edge key; a Lua script reconstructing
  the edge key from a ZSET member must translate `|` to `:`, not concatenate the member as-is, or it silently
  finds nothing - a real bug this design's conformance tests caught). `EnsureAndClaimCompensationScript` and
  `ClaimDueCompensationScript` are single atomic `EVAL`s, matching Outbox's Lua-script approach. Because the
  scripts compare `edge.Status` as a Lua *string* (`'Claimed'`, `'Completed'`, ...), any C#-side rewrite of an
  edge (`MarkCompensationPropagationCompletedAsync`/`FailedAsync`) must serialize the status enum as a string
  too (`Newtonsoft.Json.Converters.StringEnumConverter`), not Newtonsoft's default integer - another real bug
  this design's conformance tests caught, since a plain `JsonConvert.SerializeObject(intent)` there would
  silently break every later Lua string comparison against that edge.
- **Split Store** (`SplitStoreSagaStore`): all five methods are pure pass-throughs to the wrapped canonical
  relational store. This is the whole mechanism by which "the canonical store owns compensation propagation
  durability, never the Redis operational projection" holds - there is no propagation-specific code in Split
  Store at all; it inherits the canonical provider's correctness (and conformance-test coverage) automatically.

### Transaction boundary

Where a provider has a real local transaction available - a same-request `LocalAtomic` session for SQL
Server/PostgreSQL via `ILyciaPersistenceSessionAccessor` - `RelationalConnectionLease.OpenAsync` picks it up
transparently, so "log the child Compensated" and "durably claim the propagation edge" commit atomically
with each other (and with anything else the same dispatched-message handler wrote) *as a consequence of*
reusing the exact same connection-lease pattern every other SagaStore write already uses, not because of any
propagation-specific transaction code. Independent-mode relational writes, Redis, and InMemory have no such
shared-transaction guarantee - each write commits on its own - which is exactly why atomicity is not the
thing this design depends on for correctness. **The two facts do not need to commit together**: it is the
state machine and the recovery worker's own idempotent completion (never destructive) that makes the crash
window closed even when they commit separately.

### Claim/lease/stale-claim recovery

Same established pattern as `IOutboxStore.ClaimPendingBatchAsync`, deliberately chosen over Scheduling's
fencing-token model: a leased claim recorded as `(Status=Claimed, Owner, UpdatedAtUtc)`, reclaimable once
`now - UpdatedAtUtc >= recoveryTimeout`, no fencing token. This was a considered choice, not an oversight:
compensation propagation is a single durable work item advanced through a small state machine (claim →
attempt → mark outcome → time-based stale recovery), the same shape as one Outbox message - not
Scheduling's due-time-batch-with-renewable-lease shape, which is what actually needs a fencing token. Two
concurrent claimants for the same edge are mutually exclusive by construction (an atomic conditional write:
`AttemptCount < @maxAttempts AND (Status=Pending OR (Status=Claimed AND stale))`, `WITH (ROWLOCK, READPAST)` /
`FOR UPDATE SKIP LOCKED` / a Lua `EVAL` / one `lock` statement, per provider) - a worker crash after claiming
never permanently strands the edge, because the claim itself is time-bounded, not held by any live process
state.

### `CompensationWorker` - recovery-only safety net

`Lycia.Compensating.CompensationWorker` (a `BackgroundService`, mirroring `OutboxWorker`'s
`ExecuteAsync`/`RunOnceAsync`/exponential-backoff-with-jitter shape) is **not** the mandatory happy-path
executor. On the healthy path, the durable handoff and the immediate attempt happen in the same
`CompensateParentAsync` call (step 4-5 above); the worker never even sees that edge. It exists purely to
resume what the immediate path could not finish: `ClaimDueCompensationPropagationsAsync` claims a batch of
due edges (`Pending`, or `Claimed` past its stale window) each pass, and `AttemptPropagationAsync` is called
per claimed edge, with each attempt individually try/caught so one failing edge never stops the worker loop
from processing the rest of the batch or from running its next pass. `CompensationWorkerOptions`
(`BatchSize`, `MaxAttempts`, `RecoveryTimeout`, `PollInterval`, `RetryBackoff`/`MaxRetryBackoff`/`MaxJitter`,
plus `Enabled` as a pure operational kill switch for the recovery loop only) is registered unconditionally by
both `AddLycia` and `AddLyciaInMemory` - `services.AddOptions<CompensationWorkerOptions>()` plus
`services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CompensationWorker>())`, the exact
precedent `LyciaPersistenceBuilder.UseSplitStore()` set for `ReconciliationWorker`. `WithCompensationWorker(configure)`
on `LyciaPersistenceBuilder` only tunes the options (`Services.Configure(configure)`); there is no
`UseCompensationWorker()` enable call, because there is nothing to opt into - the worker is core.

### Why not `OutboxWorker` or `ReconciliationWorker`

Both were considered and rejected as the recovery mechanism, not merely unused by omission:

- **`OutboxWorker`** drives durable *outgoing transport messages* (`IOutboxStore`) - it dispatches things
  onto the event bus. Compensation propagation is an *in-process handler invocation*
  (`SagaCompensationCoordinator` calling a compensation handler directly, the same as the immediate path
  does), not an outgoing message. Reusing Outbox would mean redesigning propagation as an actual message on
  the bus, which was explicitly not undertaken here - the default, smallest-correct direction is a dedicated
  worker that shares Outbox's *pattern* (claim/lease/backoff), not its *queue*.
- **`ReconciliationWorker`** repairs the Redis operational projection from Split Store's canonical state -
  it has no concept of compensation, propagation, or handler invocation at all; it is a projection-repair
  loop for a completely different subsystem.

### Cancellation boundary

The durable handoff (`EnsureAndClaimCompensationPropagationAsync`'s atomic claim) is the dividing line.
Before it: `CompensateParentAsync` calls `cancellationToken.ThrowIfCancellationRequested()` up front, so a
pre-cancelled token prevents any mutation, including the durable claim itself, from happening at all. After
it: the durable `Claimed` record has already committed, so a cancellation (or any exception) from the
immediate `AttemptPropagationAsync` call propagates to the caller exactly as before, but it never erases or
un-claims the durable requirement - `CompensationWorker` recovers it once the lease goes stale, exactly as if
the process had crashed instead of the caller cancelling. Caller cancellation only ever stops *this specific
attempt*; it can never make the requirement to propagate disappear.

### Retry, exhaustion, and idempotency semantics

- **Retry**: a `Claimed` edge whose parent-handler invocation throws is left `Claimed` - the next pass (the
  worker, or another concurrent immediate attempt) reclaims it once `recoveryTimeout` has elapsed, up to
  `MaxAttempts` total attempts (the immediate in-request attempt, if it ran, counts as one).
- **Exhaustion**: once `AttemptCount >= MaxAttempts`, the edge is moved to the terminal `Failed` state with a
  recorded `FailureInfo` - both the relational batch-claim query and the InMemory/Redis equivalents do this
  as part of the same claim pass that discovers the exhaustion, so an exhausted edge stops being
  rediscovered by every future pass instead of silently spinning forever. It is never silently discarded:
  `GetCompensationPropagationIntentAsync(sagaId, childMessageId)` is how an operator (or a health check, or a
  future admin surface) finds it - `Status == Failed` with a non-null `FailureInfo.Reason`.
  `MarkCompensationPropagationFailedAsync` is also called directly, bypassing retry entirely, for a
  structurally unresolvable edge (see step 4-5 above) - retrying a missing/undeserializable parent record
  cannot succeed, so treating it the same as a slow-but-recoverable handler failure would just waste
  `MaxAttempts` passes before reaching the same terminal state.
- **Idempotency, and the at-least-once boundary**: framework-level state transitions are idempotent by
  construction (`MarkCompensationPropagationCompletedAsync`/`FailedAsync` are safe to call more than once;
  the store's own step-transition validation makes re-logging the same `Compensated` status a no-op) and the
  propagation intent's identity (`SagaId` + `ChildMessageId`) prevents a redelivered call from ever creating
  a second logical intent for the same edge. **Lycia does not claim exactly-once here, and never has** - if
  a worker invokes the parent's handler (the business undo runs), then crashes before
  `MarkCompensationPropagationCompletedAsync` records that fact, the next recovery pass invokes the parent's
  handler again. The framework-level bookkeeping stays consistent either way; it is the compensation
  handler's own responsibility to make its external side effects (the actual business undo - refunding a
  payment, releasing inventory, cancelling a shipment) idempotent, exactly as Lycia already expects of every
  ordinary at-least-once handler invocation. This is documented, not incidental.

### Root behavior

A root step - one with no logical parent (`ParentMessageId == Guid.Empty`) - only ever gets `MarkAsCompensated`/
`LogStepAsync(..., Compensated, ...)`. `CompensateParentAsync` returns immediately after that check, before
ever calling `EnsureAndClaimCompensationPropagationAsync`: **no propagation intent is created for a root
step**, matching `Root_Step_Compensation_Creates_No_Propagation_Intent` in
`CompensationCrashInjectionTests.cs`. `Context.MarkAsCompensated<T>(ct)` and the two-stage
`ContinueCompensation().ThenMarkAsCompensated<T>(ct)` form remain the recommended way to write a root
compensation handler precisely because they never touch propagation machinery at all.

### Branching and `ParentMessageId` lineage

Propagation follows `ParentMessageId` for the *specific edge being compensated* only - never "compensate
every other step in the saga," never a global reverse-ordered scan of all recorded steps. If step A has
children B and C, and B has child D, compensating D creates and drives exactly one edge: D → B. It has no
effect on B's *other* child C, or on C's own child E, unless and until something separately compensates C.
`Compensating_One_Child_Does_Not_Propagate_To_Or_Touch_A_Sibling` in `CompensationCrashInjectionTests.cs`
proves this directly: compensating sibling B with sibling C healthy and `Completed` leaves C's status and
propagation intent (there is none) untouched, and invokes the parent's handler exactly once - for B, never on
C's behalf.

### Crash-window boundaries this design closes

`CompensationCrashInjectionTests.cs` names each boundary directly against the state machine above (a fact is
pre-seeded exactly as a real crash would leave it, never simulated by throwing mid-call):

- **Before any compensation transition (A/B)**: nothing durable exists yet; ordinary at-least-once handler
  retry is entirely the compensation handler's own business-idempotency responsibility, as always.
- **After business compensation, before the framework's own state is durable (B)**: same as above - the
  coordinator has not run yet, so there is nothing for it to have lost.
- **After the current step's `Compensated` status is durable, before the propagation intent exists (C)** -
  the original, documented crash window: `CompensationContinuationTests.Crash_Simulated_Between_Persisting_Compensated_And_Invoking_The_Parent_No_Longer_Strands_Propagation`
  proves that a retry from exactly this pre-seeded state now invokes the parent, because propagation is
  decided by the durable intent's claim outcome, never by the child step's own status.
- **Durable intent exists, before the immediate attempt runs (D)**, and **after a claim whose owner died
  before invoking the parent (E)**: `CompensationWorker` resumes both identically, via stale-claim
  reclaiming - `Boundary_DE_CompensationWorker_Recovers_A_Stale_Claim_And_Completes_Propagation`.
- **During the parent's own business compensation (F)**: bounded, at-least-once retry -
  `Boundary_F_CompensationWorker_Retries_Until_The_Parent_Handler_Eventually_Succeeds` and
  `Boundary_F_CompensationWorker_Marks_Edge_Failed_After_MaxAttempts_Exhausted`.
- **After the parent's business undo, before the framework records completion (G)**, and **retrying an
  already-attempted edge after a simulated completion-recording crash (I)**: the parent's handler may run
  again (documented at-least-once), but the framework-level record stays a single, consistent edge -
  `Boundary_GI_Retrying_A_Claimed_Edge_After_A_Simulated_Completion_Crash_Is_Idempotent`.
- **After the parent is marked compensated, before its own further propagation completes (H)**: the *next*
  edge in the chain (parent → grandparent) is itself durable and independently recoverable, not lost just
  for being a second hop - `Boundary_H_The_Next_Propagation_Edge_In_A_Chain_Remains_Durable_And_Recoverable`.

Provider-level correctness for the state machine itself (intent creation and its idempotent identity, claim,
concurrent claim with exactly one winner, stale-claim recovery, completion, retry, attempts exhaustion) is
covered once in `Lycia.Persistence.TestKit.SagaStoreConformanceTests` and run against every provider
(InMemory, Redis, SQL Server, PostgreSQL) via each provider's own conformance-test subclass - the same
pattern already used for step-log and versioned-save conformance. `SplitStoreSagaStoreCompensationPropagationTests`
(`Lycia.Tests`) separately proves the five methods are pure delegations to the canonical store.

**Do not call this design crash-safe from its architecture description alone** - it is the crash-injection
tests and the provider conformance tests above, together, that establish it; several real bugs (the InMemory
batch-claim staleness parameter, the Redis Lua key-reconstruction separator, and the Redis enum
serialization mismatch, all noted above) were only caught by actually running these tests against real
behavior, not by reasoning about the design on paper.

---

## Configuration

The canonical registration is `AddLycia(configuration, lycia => ...)`; the callback runs `Build()`
internally. Each nested builder is concern-specific (`LyciaSagaBuilder`, `LyciaTransportBuilder`,
`LyciaPersistenceBuilder`, `LyciaMiddlewareBuilder`, and `LyciaSchedulingBuilder` from
`Lycia.Extensions.Scheduling`). `Lycia.Extensions` defines the builders, the transport-side `InMemory()`
provider and each builder's duplicate-provider guard; transport, scheduling and persistence packages add
their own extension methods, so `Lycia.Extensions` has no compile-time dependency on any of them.

```csharp
services.AddLycia(configuration, lycia =>
{
    lycia.AddSagas().FromAssemblies(typeof(SomeHandler).Assembly);
    lycia.UseTransport().RabbitMq();
    lycia.UsePersistence().WithRedisSagaStore();
    lycia.AddScheduling().WithRedisStore().WithPredefinedDelays();
    lycia.AddMiddleware().WithLogging().WithRetry().WithTracing();
    lycia.UseMessageSerializer<CustomSerializer>();
});
```

Configuration supplies values only; it never selects a provider:

```json
{
  "ApplicationId": "SampleOrderApi",
  "Lycia": {
    "EventBus": { "ConnectionString": "amqp://guest:guest@127.0.0.1:5672/" },
    "EventStore": { "ConnectionString": "127.0.0.1:6379", "LogMaxRetryCount": 5 },
    "Saga": { "DefaultIdempotency": true },
    "CommonTTL": 3600
  }
}
```

`Lycia:EventBus` binds to `EventBusOptions` and `Lycia:EventStore` to `SagaStoreOptions`; the provider is
still chosen in code.

The flat `AddLycia(configuration).AddSagasFromCurrentAssembly().Build()` form and `AddLyciaRabbitMq()`,
`AddLyciaNats(...)`, `AddLyciaKafka(...)`, `AddLyciaScheduling(...)` and `AddLyciaInMemoryScheduling(...)`
are `[Obsolete]` wrappers over the same registration logic.

---

## Testing

| Project | Scope | Needs Docker |
| --- | --- | --- |
| `Lycia.Tests` | Unit and component tests (net9.0, net10.0); a few Testcontainers tests | partly |
| `Lycia.Tests.NetFramework` | The same core on net48 | partly (Redis) |
| `Lycia.Persistence.TestKit` | Shared conformance suites for SagaStore, Inbox and Outbox providers | — |
| `Lycia.Persistence.InMemory.Tests` | TestKit against InMemory, plus Outbox retry/exhaustion tests | no |
| `Lycia.Persistence.Redis.Tests`, `.SqlServer.Tests`, `.PostgreSql.Tests` | TestKit and provider-specific tests (atomic boundary, failure windows, Split Store, journal) against real engines | yes |
| `Lycia.IntegrationTests`, `Lycia.IntegrationTests.NetFramework` | RabbitMQ/Redis transport and compensation integration | yes |
| `Lycia.Extensions.AspNetCore.Tests` | `MapLyciaDiagnostics()` endpoint tests (in-memory `TestServer`) | no |

Every provider must pass the same TestKit suites, which is what keeps step-transition validation,
idempotency, concurrency, Inbox takeover and Outbox claim semantics identical across providers. Inbox and
Outbox claim SQL and Lua only run against real engines in the provider suites, so changes to them must be
validated there.

The CI workflow runs `Lycia.Tests`, `Lycia.Extensions.AspNetCore.Tests`, `Lycia.IntegrationTests`, the
four provider suites (`Lycia.Persistence.{InMemory,Redis,PostgreSql,SqlServer}.Tests`) and the two
NetFramework projects on every `main`/`dev` push and `v*` tag. The Microservices end-to-end run is part of
release validation. The
container-backed suites pull every image from [`infrastructure-versions.json`](infrastructure-versions.json)
(see [Supported infrastructure versions](#supported-infrastructure-versions)); no test hard-codes an image.

The two NetFramework projects run on Windows runners against native services (Memurai for Redis, the
Chocolatey RabbitMQ package), which cannot be pinned to the contract. They cover the net48 / `RabbitMQ.Client`
6.x code path, and the same projects run against the pinned brokers locally through Testcontainers (they
choose a container unless `CI=true`), which is how that path is validated against the contract's versions.

**Microservices end-to-end.** `samples/Microservices` runs five services on RabbitMQ, PostgreSQL,
per-service Redis and Jaeger. [MANUAL_TESTING.md](samples/Microservices/MANUAL_TESTING.md) covers the
happy path with canonical, journal, Redis, Inbox, Outbox and reconciliation checks; Redis outage and
recovery; projection deletion and journal rebuild; duplicate delivery; Inventory and Payment failure;
process restart; RabbitMQ reset; and Jaeger trace inspection. `reset-state.sh` resets per-service
PostgreSQL and Redis state, and RabbitMQ only when explicitly requested.

---

## Supported infrastructure versions

Lycia's server compatibility is a contract, kept in one machine-readable file,
[`infrastructure-versions.json`](infrastructure-versions.json), and mirrored in the README table. Each
integration (RabbitMQ, Redis, PostgreSQL, SQL Server, Kafka, NATS) has a `minimum` and a `current` entry,
each a version series plus the exact image tag that is run for it. Tags are always explicit; `latest` is
rejected.

### Four different words

- **Technical floor** — the oldest version the implementation could work on, derived from the features it
  uses (for example `SKIP LOCKED` needs PostgreSQL 9.5). Analysis only; can be measured, is not a promise.
- **Supported minimum** — the oldest version Lycia commits to.
- **Tested minimum** — the oldest version the automated suites run against.
- **Tested current** — the recent version every CI run exercises.

A version is *supported* only if it is *tested*: the supported minimum must equal the tested minimum. A
version between the technical floor and the supported minimum is "technically compatible" at best, and the
documentation must never blur that into "supported".

### How a minimum is chosen

The supported minimum is the **higher** of

1. the technical floor (proved by implementation analysis, and where feasible by running the suite or the
   offending statement against the older version), and
2. the oldest series the vendor still supports at the time of the decision (from the vendor's own lifecycle
   documentation, never from memory).

It is accepted only after the complete container-backed suites pass on it. Where a vendor publishes no
support policy (NATS), the minimum is the technical floor, and the README says that no vendor policy backs
it. A minimum that cannot be proven by analysis plus documentation plus a test run is recorded as
*unresolved* in the ledger rather than guessed.

### Test tracks and CI cost

- The default `current` track runs on every push in the existing jobs (`unit-tests`, `integration-tests`,
  `provider-tests`).
- Setting `LYCIA_TEST_INFRA_TRACK=minimum` runs the same suites against the `minimum` images. The
  `compatibility-minimum` job does this for `Lycia.Tests`, the Redis/PostgreSQL/SQL Server provider suites
  and `Lycia.IntegrationTests`. It is not repeated on every push: it runs weekly (Monday, from the default
  branch), on `workflow_dispatch`, and for every `v*` tag, and the `publish` job needs it, so a release
  cannot ship with the minimum untested.
- `LYCIA_TEST_{RABBITMQ,REDIS,POSTGRESQL,SQLSERVER,KAFKA,NATS}_IMAGE` overrides one image. This is how a
  candidate version, or a registry mirror, is tried without editing the contract.
- The CI service images, `docker-compose.yml` and `samples/Microservices/docker-compose.yml` run the
  `current` images. `InfrastructureContractTests` fails if any of them, a test project or the README table
  disagrees with the file, so a container tag cannot be bumped without deciding what it means for
  compatibility.

### Raising or changing a version

- **Raising the current version** (a new vendor release): edit `current` in the file, run the full suites
  on it, and update the README table. This never changes what is supported.
- **Raising the minimum** is a compatibility change: it needs a ledger entry, a release note, and at least a
  minor version bump; do it when the vendor ends support for the series, not before. It is never a side
  effect of a tag bump, an image refresh or a dependency update.
- **Lowering the minimum** requires the complete suites to pass on the older version first.
- A new release **tag never changes compatibility**. Compatibility only changes through this file.

Known upcoming change: PostgreSQL 14 reaches its final release on 12 November 2026, after which the
PostgreSQL minimum should be reviewed against this rule.

Not supported, and not implemented: RabbitMQ Streams and Super Streams, Kafka Share Groups (KIP-932) and
Redis Cluster. They appear in no supported column and are tracked in the ledger.

---

## Release process

### Versioning

Versions come from Nerdbank.GitVersioning. `version.json` holds the base version (`1.18.0` is the target
first stable release), and `publicReleaseRefSpec` (`^refs/tags/v\d+\.\d+\.\d+$`) makes a build of a
matching tag a public release with exactly that version. Builds of branches carry a prerelease suffix.

### Release contract

- Pushes to `main` and `dev` run CI (build and tests) only and never publish.
- Publishing is driven only by a release tag matching `v*`. The `publish` job runs only when
  `github.ref` starts with `refs/tags/v` for a `push` event (a schedule or manual run never publishes),
  and only after all test jobs pass, including the minimum-version compatibility job.
- Before publishing, the job requires the packed `Lycia` version to equal the tag without its `v`, and
  validates that exactly the twelve public packages were produced at that version, that no package
  depends on an internal project, and that the relational providers embed
  `Lycia.Persistence.Relational.Internal.dll`. Internal and test projects are never packed or pushed.
- All twelve packages are pushed with `--skip-duplicate`, so rerunning a release never republishes an
  existing version.

### NuGet Trusted Publishing

The workflow authenticates to nuget.org with NuGet Trusted Publishing over GitHub Actions OIDC. It does
not use a long-lived API key or any repository secret.

- Only the `publish` job has `id-token: write` (plus `contents: read`); the workflow default is
  `contents: read`.
- The official `NuGet/login@v1` action exchanges the job's GitHub OIDC token for a short-lived nuget.org
  API key (`user: okutucu`), which the push step uses.
- nuget.org accepts the token only if it matches the trusted publishing policy:

| Setting | Value |
| --- | --- |
| Policy name | `lycia-release` |
| Package owner | `okutucu` |
| CI/CD provider | GitHub Actions |
| Repository owner | `gokayokutucu` |
| Repository | `lycia` |
| Workflow file | `dotnet.yml` |
| Environment | none |
| Package scope | `Lycia*` |
| Permission | Push new packages and package versions |

Renaming the workflow file, moving the repository, or adding an `environment:` to the job requires
updating the policy first.

### Releasing a version

1. Integrate the milestone into `main` as described in `AGENTS.md` (only when the ledger's
   `FINALIZATION` status allows it).
2. Confirm `version.json` holds the intended version, and that `nbgv get-version` for a simulated
   `refs/tags/v<version>` and a local `dotnet pack` both produce exactly that version.
3. Push `main` and wait for CI to pass.
4. Tag the validated `main` commit `v<version>` and push the tag.
5. Watch the workflow, then confirm all twelve packages at that version on nuget.org.

---

## Naming conventions

- **RabbitMQ**: command queue `command.{MessageType}.{ApplicationId}` bound by the marker-derived owner;
  event queue `event.{MessageType}.{HandlerType}.{ApplicationId}` on a fanout exchange; response queue
  `response.{MessageType}.{ApplicationId}`; exchanges `{event|command|response}.{MessageType}`.
- **Headers**: `lycia-type`, `lycia-schema-id`, `lycia-schema-ver`, plus `CorrelationId`, `SagaId` and
  `MessageId` on every message.
- **Middleware slots**: `ILoggingSagaMiddleware`, `IRetrySagaMiddleware`, `ITracingSagaMiddleware`.
- **Handler discovery**: `_LyciaHandlerDiscovery` (`SafeGetTypes`, `IsSagaHandlerBase`,
  `ImplementsAnySagaInterface`, `GetMessageTypesFromHandler()`).

---

For questions or contributions, open an issue or start a discussion on the project repository.
