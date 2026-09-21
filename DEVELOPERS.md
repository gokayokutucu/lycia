# Lycia Developer Documentation

This document describes how Lycia works internally, how to develop and test it, and how it is released.
It complements the user-facing [README.md](README.md). [PROJECT_LEDGER.md](PROJECT_LEDGER.md) records the
locked architectural decisions, deferred work and release readiness; [AGENTS.md](AGENTS.md) defines the
Git workflow.

---

## Repository and package layout

Eleven packages are published. Four projects are internal and never published on their own:

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
`Lycia.Persistence.PostgreSql` (`net8.0;net9.0;net10.0`). Code must compile on `netstandard2.0`: for
example, use `set` rather than `init` accessors in shipped code (no `IsExternalInit`).

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

Only `IConfirmedEventBus` success becomes `Published`. Kafka (`EnableIdempotence`, `Acks.All`) and NATS
JetStream implement it. RabbitMQ and Core NATS do not, so an accepted publish becomes
`ConfirmationUnknown`. A transport exception is also `ConfirmationUnknown`, because the broker may have
received the message before the failure. Only a permanent local error before any publish — envelope,
type resolution or serialization — becomes `Failed`.

This is deliberately conservative: a message is never recorded as delivered without a positive
confirmation, and the cost is redelivery, which the at-least-once contract and the Inbox already absorb.
RabbitMQ's `ConfirmationUnknown` is the current validated behavior of the transport integration, not a
fixed architectural limit; implementing confirmation requires awaiting per-publish broker confirmation
inside `RabbitMqEventBus` and is tracked in the ledger backlog.

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

---

## Saga patterns

| Pattern | State | Handlers |
| --- | --- | --- |
| Choreography (reactive) | Stateless, no `TSagaData`; compensation through `ISagaCompensationHandler<T>` | `StartReactiveSagaHandler<TStart>`, `ReactiveSagaHandler<TMessage>` |
| Sequential orchestration (coordinated) | `TSagaData`; failures compensate through `CompensateAndBubbleUp()` | `StartCoordinatedSagaHandler<TStart, TSagaData>`, `CoordinatedSagaHandler<TMessage, TSagaData>` |
| Request-response orchestration | `TSagaData`; each step sends a command and continues on the response | `StartCoordinatedResponsiveSagaHandler<TStart, TResponse, TSagaData>`, `CoordinatedResponsiveSagaHandler<TMessage, TResponse, TSagaData>`, `IResponseSagaHandler<TResponse>` |

`Sample.Order.Orchestration.Consumer` and the Microservices sample use request-response orchestration.

Reactive handlers guard against duplicates with `Context.IsAlreadyCompleted<T>()`, governed by
`SagaOptions.DefaultIdempotency` (default `true`) and overridable per handler through
`EnforceIdempotency`.

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

Every provider must pass the same TestKit suites, which is what keeps step-transition validation,
idempotency, concurrency, Inbox takeover and Outbox claim semantics identical across providers. Inbox and
Outbox claim SQL and Lua only run against real engines in the provider suites, so changes to them must be
validated there.

The CI workflow runs `Lycia.Tests`, `Lycia.IntegrationTests` and the two NetFramework projects. The
provider suites and the Microservices end-to-end run are part of release validation.

**Microservices end-to-end.** `samples/Microservices` runs five services on RabbitMQ, PostgreSQL,
per-service Redis and Jaeger. [MANUAL_TESTING.md](samples/Microservices/MANUAL_TESTING.md) covers the
happy path with canonical, journal, Redis, Inbox, Outbox and reconciliation checks; Redis outage and
recovery; projection deletion and journal rebuild; duplicate delivery; Inventory and Payment failure;
process restart; RabbitMQ reset; and Jaeger trace inspection. `reset-state.sh` resets per-service
PostgreSQL and Redis state, and RabbitMQ only when explicitly requested.

---

## Release process

### Versioning

Versions come from Nerdbank.GitVersioning. `version.json` holds the base version (`1.18.0` is the target
first stable release), and `publicReleaseRefSpec` (`^refs/tags/v\d+\.\d+\.\d+$`) makes a build of a
matching tag a public release with exactly that version. Builds of branches carry a prerelease suffix.

### Release contract

- Pushes to `main` and `dev` run CI (build and tests) only and never publish.
- Publishing is driven only by a release tag matching `v*`. The `publish` job runs only when
  `github.ref` starts with `refs/tags/v`, and only after all test jobs pass.
- Before publishing, the job requires the packed `Lycia` version to equal the tag without its `v`, and
  validates that exactly the eleven public packages were produced at that version, that no package
  depends on an internal project, and that the relational providers embed
  `Lycia.Persistence.Relational.Internal.dll`. Internal and test projects are never packed or pushed.
- All eleven packages are pushed with `--skip-duplicate`, so rerunning a release never republishes an
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
5. Watch the workflow, then confirm all eleven packages at that version on nuget.org.

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
