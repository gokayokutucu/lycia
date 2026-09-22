# PROJECT LEDGER

Milestone: **Persistence / Reliability Architecture**

This ledger records phase order and final-integration authority. `AGENTS.md` defines the operational
Git rules. Agents must update this file as phases move through the milestone.

# LOCKED

- Normal phase work starts from the latest local `dev` and merges back only to `dev`.
- `main` is updated only at an authorized, fully validated milestone boundary.
- Messaging and delivery remain transport-independent and at least once; do not claim exactly-once delivery.
- The default persistence-boundary policy is `Auto`. Do not add `WithAutomaticConsistency()`,
  `WithStrongConsistency()`, or `AllowNonAtomicBoundary()`.
- The only explicit persistence-boundary overrides are `RequireAtomicBoundary()` and
  `UseIndependentTransactions()`.
- Compatible relational providers using the same database identity may resolve to `LocalAtomic`
  automatically. Mixed providers or different database identities resolve to `Independent` automatically.
- Atomicity is service-local: never attempt to include service A's Outbox and service B's Inbox in one transaction.
- Application business tables are not automatically included in the Lycia atomic persistence boundary.
- Scheduling owns delayed intent until due, then hands the original Send/Publish/Respond semantic to
  the outgoing pipeline. Do not add a competing Outbox timer record.
- Atomic SagaStore + Inbox + Outbox behavior must be honest about provider capabilities. Do not
  represent InMemory or Redis as sharing a relational transaction.
- Stable message identities and Inbox idempotency are the duplicate-delivery safety boundary.
- In Split Store mode, relational persistence is canonical and Redis is rebuildable operational state;
  reconciliation never turns Redis into request-path authority.
- Replay/rebuild must be deterministic and must not invoke business handlers.
- In a deferred/composite fluent chain (`SendWithTracking`/`PublishWithTracking`/`RespondWithTracking`/
  `ScheduleWithTracking` → `ISagaStepFluent.Then...`; `ContinueCompensation()` →
  `ICompensationContinuation`/`ICompensatedContinuation`), the entry method never accepts a
  `CancellationToken` and only the terminal method does; that one token governs the whole deferred
  operation. Do not reintroduce a captured/fallback token at the entry method.
- Coordinated compensation continuation is `Context.ContinueCompensation().ThenMarkAsCompensated<TStep>()`
  (defers) `.ThenBubbleUp(cancellationToken)` (terminal; propagates to the logical parent) — or the
  two-stage `.ThenMarkAsCompensated<TStep>(cancellationToken)` (terminal; does not propagate). Do not have
  `ThenBubbleUp` also call `MarkAsCompensated` first: the internal bubble-up primitive it calls already
  logs the step Compensated as one step of its own durable propagation walk. That primitive
  (`IBubbleUpCompensationPrimitive`, `Lycia.Saga.Abstractions.Compensating`) is deliberately internal, not
  a member of `ISagaContext` — never re-add a public `Context.BubbleUpCompensationAsync<TStep>(...)` or any
  equivalent direct-call escape hatch; `ThenBubbleUp` must remain its only application-facing entry point.
- Compensation propagation durability is part of `ISagaStore` correctness, not an optional add-on: the
  current step's `Compensated` status is never treated as proof that propagation to its logical parent
  completed, started, or is even required — those are separate durable facts
  (`CompensationPropagationIntent`, state machine Pending → Claimed → Completed/Failed, identity
  `SagaId + ChildMessageId`). `CompensationWorker` (recovery-only; the immediate in-request attempt is the
  happy path) is registered unconditionally by `AddLycia`/`AddLyciaInMemory`, the same precedent
  `LyciaPersistenceBuilder.UseSplitStore()` set for `ReconciliationWorker`. A root step
  (`ParentMessageId == Guid.Empty`) never creates a propagation intent. Do not reintroduce a guard that
  treats an already-`Compensated` step as proof propagation is unnecessary — that is the exact mechanism
  the closed crash window (see `COMPLETED`) depended on. See DEVELOPERS.md, "Coordinated compensation
  continuation", for the full state machine, persistence authority per provider, and crash-injection test
  mapping.

# ACTIVE

(No phase active. All implementation phases, the architecture review, the reliability red team and
final validation are complete; see `FINALIZATION`.)

# NEXT

(No further phases queued behind Phase 7 at this time.)

# HOLD / BACKLOG

- ~~Durable, crash-safe coordinated compensation propagation (bubble-up)~~ — **CLOSED**, see `COMPLETED`
  ("Durable compensation propagation" entry) and DEVELOPERS.md, "Coordinated compensation continuation".
  `CompensateParentAsync` no longer treats the child step's own `Compensated` status as proof that parent
  propagation happened; a durable `CompensationPropagationIntent` per edge, claimed atomically and resumed
  by a dedicated `CompensationWorker` on crash, replaces that guard. Proven by
  `CompensationCrashInjectionTests.cs` and `CompensationContinuationTests.cs`, plus provider conformance
  coverage in `Lycia.Persistence.TestKit.SagaStoreConformanceTests` run against every built-in provider.
- **PostgreSQL WAL / logical decoding CDC projection feed (future evaluation, not a current-release change or committed roadmap item):** In a PostgreSQL deployment, canonical state changes could be exposed through WAL (*Write-Ahead Log*) logical decoding and CDC (*Change Data Capture*) and consumed to update the Redis operational projection. This would provide a durable feed of **committed** PostgreSQL changes; it would not make PostgreSQL and Redis atomic or eliminate normal replication lag. Redis remains a rebuildable, eventually consistent projection, and its writer must retain version fencing/CAS so delayed or redelivered records cannot overwrite a newer projection. This is distinct from the Journal: `Journal != WAL`; the Journal remains Lycia's framework-level canonical transition history and deterministic rebuild source, while WAL/CDC is only a database change-propagation mechanism. The current provider-neutral `ReconciliationIntent + ReconciliationWorker` remains the default. A possible later boundary is `IProjectionChangeFeed`, with `ReconciliationIntentFeed` and an opt-in PostgreSQL-only `PostgreSqlLogicalReplicationFeed`; only one feed may write a deployment's projection. Candidate consumers include Debezium and a custom logical-replication consumer. Any evaluation must explicitly own replication-slot lag, retained-WAL/disk-pressure, consumer health, LSN progress, recovery, and monitoring. CDC could also be evaluated as an Outbox relay transport (WAL/CDC to broker) while retaining the transactional Outbox record itself and its at-least-once/confirmation semantics. Because Lycia is provider-neutral, this must remain an optional PostgreSQL strategy, never a core requirement.
- Redis Cluster hash-slot-safe multi-key atomicity: inventoried during Phase 7 (Redis Inbox/Outbox
  Lua scripts touch multiple keys — `outbox:msg:{id}`, `outbox:pending` — without hash-tag key naming,
  so cross-key atomicity is not Cluster-safe as written). No real Redis Cluster was available to
  validate a fix, so this stays explicitly on hold rather than being claimed as supported.
- **Deferred, not implemented and not supported (must not appear in any supported-version column):**
  RabbitMQ Streams and Super Streams (the RabbitMQ transport is AMQP 0-9-1 only); Kafka Share Groups
  (KIP-932; not yet a stable Kafka feature).
- **Pending spikes (not started, no code):** a `Lycia.Extensions.Kafka.Preview` package concept spike (a
  separate opt-in package would be the only way to expose preview broker features); and a Messaging
  Semantics Architecture Spike (Queue / Stream / PubSub abstractions and what each transport can honestly
  promise). Neither is scheduled.
- Infrastructure contract follow-ups: PostgreSQL 14 reaches its final release on 12 November 2026, after
  which the PostgreSQL minimum must be reviewed under the DEVELOPERS.md policy; the NATS minimum (2.9) rests
  on the technical floor because NATS publishes no server support policy; the Windows CI jobs (Memurai and
  the Chocolatey RabbitMQ package) cannot be pinned to the contract; Kafka's minimum is run through
  `apache/kafka:3.8.0` (the Confluent image for the equivalent series could not be pulled during validation)
  and technical floors below the tested minimums (RabbitMQ < 3.12, Kafka < 3.7, SQL Server < 2017,
  PostgreSQL < 14, Redis < 6.2) are analysis only.
- Redis rows already stranded by 1.18.0: before the 1.18.1 fix the Redis claim script removed a stale
  `Publishing` entry at the attempt cap from the pending set for good, so such a row has a message record
  but no pending entry and the fixed script cannot rediscover it. SQL Server and PostgreSQL rows in that
  state recover on their own once the fix is deployed. For Redis, re-add the entry to the pending set
  (`ZADD <namespace>:pending 0 <messageId>`); the next claim then recovers it (covered by a test).
- Outbox (Low): a store failure while recording the outcome of a *delivered* final attempt can be handled
  as a failed publish and end in a false `Abandoned` (operator alert, no loss). The dispatcher does not
  separate transport failures from persistence failures. Crash loops that occur after a claim but before
  `MarkPublishingAsync` are not bounded by `MaxAttempts` because no attempt is started; a store or payload
  that crashes there every time would be reclaimed forever. Lowering `MaxAttempts` while rows rest at
  `ConfirmationUnknown` below the old cap makes those rows rest as well.
- `Lycia.Extensions` imposes Autofac, Serilog and Apache.Avro on every consumer transitively (dependency
  weight, not correctness).
- Packages ship without symbol packages (`.snupkg`).
- Test-only: Testcontainers pulls SSH.NET 2024.2.0, flagged NU1903 (GHSA-q939-rpr3-3284) in the test
  projects. No shipped package is affected.
- Workflow explorer and operational visualization.

# COMPLETED

- **Phase 1 — SagaStore providers:** InMemory, Redis, SQL Server, and PostgreSQL provider architecture,
  optimistic concurrency, DSL, and shared conformance tests. Feature result `4e3f37c`; merged into
  `dev` as `618d3ba`.
- **Phase 2A — Inbox/Outbox foundation:** provider-neutral contracts, persistence-session foundation,
  InMemory stores, Inbox dispatch hook, and DSL extension points. Feature commit `56419df`; merged
  into `dev` as `dcbc526`.
- **Phase 2B — Durable Inbox/Outbox providers:** Redis, SQL Server, and PostgreSQL durable stores,
  concurrent-safe claiming, lifecycle states, and dispatcher foundation. Feature result `3c73cab`;
  merged into `dev` as `e3f4a57`.
- **Phase 3 — Outbox pipeline integration:** `IOutgoingMessagePipeline`, direct-versus-Outbox
  selection, durable Send/Publish/Respond capture, semantic Outbox dispatch, a hosted worker with
  bounded retry/backoff, stable `MessageId`, confirmation distinction where supported, and preserved
  scheduling ownership. Feature commit `4939c70`; merged into `dev` as `40a6811`.
  Historical note: this Phase 3 state was also merged into `main` as `6af4d9b` before the
  milestone-only workflow was adopted; it does not authorize future per-phase `main` merges.
- **Roadmap maintenance:** aligned this milestone with the Atomic Persistence Boundary phase model.
  Feature commit `9e5fee3`; merged into `dev` as `56e60db`.
- **Phase 4 — Atomic Persistence Boundary:** automatic topology resolution with default `Auto`,
  `RequireAtomicBoundary()`/`UseIndependentTransactions()` policy overrides, safe normalized database
  identities, and a service-local shared SQL Server/PostgreSQL Inbox + SagaStore + Outbox transaction.
  Rollback fault windows, indeterminate commit handling, provider regressions, and package consumers
  were validated without cross-service or exactly-once claims. Feature commit `3ce6eaf`; merged into
  `dev` as `a0ab41e`.
- **Phase 5 — Split Store + Reconciliation:** explicit relational-canonical/Redis-operational topology,
  canonical-transaction reconciliation intents, bounded claim/retry recovery, version-fenced idempotent
  Redis projection, current-state restoration without handlers, PostgreSQL and SQL Server providers,
  and a real five-service RabbitMQ/PostgreSQL/Redis sample. Redis outage, restoration, duplicate delivery,
  downstream failure boundaries, targeted responses, and local package consumers were validated without
  dual-write, cross-service-transaction, replay, or exactly-once claims. Feature commits `d90db45` and
  `4a80258`; merged into `dev` as `ae66b3e`.
- **Phase 6 — Canonical Journal + Replay / Rebuild:** an append-only, immutable canonical transition
  journal (SQL Server `dbo.LyciaSagaJournal`, PostgreSQL `lycia_saga_journal`; Redis is the rebuild
  target, never the canonical journal store) distinct from the Phase 5 reconciliation intent, which
  only re-queues the latest row and is not retained ordered history. `SagaId` + `SequenceNumber` is the
  ordering authority, deliberately identical to the existing `SagaData.Version` counter rather than a
  second axis. Journal append happens inside `SplitStoreSagaStore` in the same call that already writes
  the reconciliation intent, so it commits or rolls back with Inbox/SagaStore/Outbox as one
  `LocalAtomic` unit; `UseSplitStore()` now requires a registered `ISagaJournalStore`. A pure,
  side-effect-free `ISagaJournalReducer` (each entry carries a full post-transition SagaData + step-log
  snapshot, not a delta) and a single `ISagaRebuildService` (rebuild one/all with per-saga failure
  isolation, progress, cancellation, a resumable cursor; non-mutating verify with
  Healthy/MissingProjection/VersionMismatch/StateMismatch/JournalGap/SchemaUnsupported/CorruptEntry
  classification) reuse the existing `IOperationalSagaProjectionStore` CAS/version-fencing writer for
  installation, so rebuild and normal reconciliation share one Redis-installation guarantee.
  `IJournalEntryUpcaster` schema evolution, continuity/corruption detection, and a construction-based
  side-effect-isolation proof (no handler/transport/Inbox/Outbox dependency in `SagaRebuildService`) were
  added. SQL Server and PostgreSQL real-container tests validated atomic append/rollback (no phantom
  journal history), concurrent-same-version single-winner append, idempotent duplicate-transition
  append, and rebuild-after-Redis-loss against real Redis. A full live Microservices docker-compose HTTP
  E2E was not run; the equivalent proof was validated at the persistence-provider integration-test
  level instead, and is recorded as a Phase 7 follow-up if a live run is later wanted. Feature commits
  `9cfbb46`, `c55e8f2`, `5c6c578`, `484c78c`, `6db807c`, `b5d0c81`; merged into `dev` as `ba04e48`.
- **Phase 7 — Reliability Hardening:** stale-ownership audit across scheduling, Vacuum, and Split Store
  reconciliation/rebuild confirmed each already used correct fencing-token CAS (owner and fence both
  validated, not lease-expiry alone) — no new distributed-lease mechanism was added. The one real gap
  found was scheduling dispatch retrying immediately on failure instead of backing off; `SchedulerWorker`
  now uses the same bounded exponential-backoff-plus-jitter shape `OutboxWorker` already had
  (`SchedulerWorkerOptions.MaxRetryBackoff`/`MaxJitter`). Public API: `LyciaSchedulingBuilder.WithWorker(...)`
  renamed to `WithDispatch(...)` (canonical DSL, documented by behavior not the internal `SchedulerWorker`
  class); `WithWorker(...)` kept as an `[Obsolete]` thin wrapper over the same options, no duplicated
  logic; `WithVacuum(...)`/`VacuumOptions` left unchanged as instructed. Added
  `ILyciaReliabilityDiagnostics`/`LyciaReliabilitySnapshot`, a safe secret-free topology snapshot
  composed from existing signals (`IPersistenceTopology`, Inbox/Outbox/journal/rebuild registration
  presence) rather than a new tracked state. RabbitMQ publisher confirms were investigated and
  deliberately not implemented (see HOLD/BACKLOG) — a correctness-over-checkbox decision explicitly
  permitted by this phase's own instructions. NATS/Kafka confirmation semantics were verified unchanged
  and honest. Added SQL Server/PostgreSQL failure-window tests covering
  `PersistenceCommitOutcomeUnknownException` wrapping, reconnect-after-pool-reset, and a genuine journal
  unique-constraint violation rolling back the whole `LocalAtomic` transaction including the canonical
  save. A live Microservices docker-compose E2E (deferred from Phase 6) ran all 7 required scenarios —
  happy path, Redis outage/recovery, process restart, Redis-projection-delete + journal rebuild,
  duplicate delivery, Inventory failure, Payment failure — and in doing so found and fixed a real bug:
  `PostgreSqlSagaStore`/`SqlServerSagaStore.LoadSagaDataAsync` eagerly wrote an unjournaled canonical
  version-1 row on first Load, causing a permanent journal gap (`JournalGap` on every fresh saga's
  `/verify`); Load is now a pure read, and the real version-1 write happens through the first explicit
  Save, correctly journaled. A conformance-suite bug found during final validation (a new stale-writer
  test wrongly assumed two `CreateStore()` instances share canonical state, true for Redis/SQL
  Server/PostgreSQL but false for `InMemorySagaStore`'s isolated in-process storage) was also fixed.
  Full README.md and DEVELOPERS.md top-to-bottom audits corrected stale "future" claims (Split Store,
  journal, diagnostics were already implemented), updated the canonical scheduling example to
  `WithDispatch(...)`, and documented the RabbitMQ `ConfirmationUnknown` decision plainly. All shipped
  packages, `Lycia.Tests`, and `Lycia.IntegrationTests` gained `net10.0` alongside their existing target
  frameworks. Final validation: solution builds with zero warnings (Debug and Release, all TFMs
  including net10.0); `Lycia.Tests` 177/177 (net9.0 and net10.0); `Lycia.Tests.NetFramework` 46/46
  (net48); SQL Server persistence suite 55/55 (net8.0 and net9.0); PostgreSQL persistence suite 55/55
  (net8.0 and net9.0); InMemory persistence suite 45/45 (net9.0); all affected packages pack cleanly
  including a net10.0 lib folder; `git diff --check` clean. Feature commits `035ef0e`, `ce4f19a`,
  `4a02432`, `86da67f`, `4f3f744`, `ea81f09` (plus `69b61e2` for the `WithDispatch` rename and retry
  backoff parity); merged into `dev` as `6e6d989`.

- **Finalization — review, red team, remediation and release preparation:** see `REMEDIATION` for
  the findings and fixes. Release preparation: conformance-test isolation fix (`6f9436a`, merged
  `309c716`); NuGet Trusted Publishing (`c164038`, merged `061ace6`); release version `1.18.0`
  (`31b4a19`, merged `3ef7cb9`); Outbox abandon refinement (`aa1e543`, merged `065e963`); handler
  failure visibility (`07692b5`, merged `9fb791e`); documentation release audit and CI on `dev`
  (`cbe600e`, `955bc6d`, `c0155e2`, merged `925c6d2`).
- **Post-1.18.0 patch — Outbox final-attempt crash recovery:** a worker that died on the last permitted
  attempt left a `Publishing` row at the attempt cap that no claim query returned, so the message was
  neither retried nor terminal (Medium; found after the 1.18.0 release). The attempt cap now gates only the
  statuses a new attempt starts from; a stale `Claimed`/`Publishing` row is handed back whatever its
  `RetryCount`, atomically, in all four providers. The dispatcher republishes an in-doubt final attempt once
  with the same `MessageId` (at-least-once), abandons a lost recovery attempt instead of looping, and leaves
  a shutdown-cancelled final attempt for recovery. No new counter and no schema change. Verified on real
  Redis, SQL Server and PostgreSQL and end to end on the Microservices stack. Feature commit `a3797c8`;
  merged into `dev` as `475f19a`. Not released: a `1.18.1` tag, `main` merge and publication are pending an
  explicit release decision.
- **Post-1.18.0 patch — RabbitMQ publisher confirms and the supported-infrastructure contract (1.18.1):**
  *Publisher confirms.* The RabbitMQ transport now publishes on confirm-enabled channels (RabbitMQ.Client
  7.1.2 `CreateChannelOptions` + tracked `BasicPublishAsync` on net8+; 6.8.1 `ConfirmSelect` with a
  sequence-number tracker on netstandard2.0 — the built-in `WaitForConfirms` reports a nack as an ack, which
  was measured). Messages are published `mandatory`; a `basic.return` is an unroutable failure and is never a
  confirmation. `IConditionalConfirmedEventBus` lets a transport declare whether confirmations are available
  at runtime (NATS: only with JetStream), so the Outbox stays honest for Core NATS. Outcomes: ack ->
  `Published`; nack, return or a definite failure -> the existing failure/retry path;
  connection loss or confirm timeout -> `ConfirmationUnknown`. Delivery remains at-least-once. Verified against
  real RabbitMQ (nack, unroutable, unavailable, dropped connection, lost confirm, broker restart, Send/Publish/
  Respond, cancellation) on both client generations, and with the crash-recovery patch above unchanged.
  *Infrastructure contract.* `infrastructure-versions.json` is the single source of the supported minimum and
  tested current version of RabbitMQ, Redis, PostgreSQL, SQL Server, Kafka and NATS; test images, CI, compose
  files and the README table are guarded against it (`InfrastructureContractTests`), and a minimum-track CI
  job (weekly, on demand and before every release tag) runs the suites on the minimum versions. Policy is in
  DEVELOPERS.md. Minimum = the higher of the technical floor and the oldest vendor-supported series, and only
  what is tested is supported. A release tag never changes compatibility. Validated on both tracks (current:
  RabbitMQ 4.3, Redis 8.10, PostgreSQL 18, SQL Server 2025-CU9, Kafka 4.3, NATS 2.15; minimum: RabbitMQ 3.13,
  Redis 6.2, PostgreSQL 14, SQL Server 2017-CU31, Kafka 3.8, NATS 2.9): `Lycia.Tests` 184/184,
  `Lycia.Tests.NetFramework` 46/46, InMemory 83/83, Redis 49/49, SQL Server 69/69, PostgreSQL 69/69,
  `Lycia.IntegrationTests` 46/46 (net9.0, net10.0) and `.NetFramework` 26/26 (net48), plus a live
  Microservices run in which every Outbox row settles as `Published` with one attempt (previously
  `ConfirmationUnknown` at the attempt cap); broker outage, SIGKILL, requeued duplicate (absorbed by the
  Inbox) and a stale `Publishing` row at the attempt cap (recovered once) left no stranded row and a healthy
  journal. Feature commits `9c1db21` (publisher confirms) and `5ee2e13` (infrastructure contract); merged into
  `dev` as `3c20278`. Not released: no `v1.18.1` tag,
  `main` merge or publication yet.
- **Post-1.18.0 patch — ASP.NET Core reliability diagnostics endpoint:** `app.MapLyciaDiagnostics()`
  (default `GET /diagnostics/lycia`, or a custom path) is a thin, opt-in HTTP projection of the existing
  `ILyciaReliabilityDiagnostics.GetSnapshot()` - delivery guarantee, persistence mode/provider, Split
  Store canonical/operational stores, and Inbox/Outbox/journal/reconciliation flags - never a second
  topology-inference engine. It is a configuration/topology endpoint, not a health check: no network
  calls, no probe of configured infrastructure, normally `200 OK`. `AddLycia(...)` never maps it.
  **New public package (12th):** `Lycia.Extensions.AspNetCore` (`net8.0;net9.0;net10.0` only, with
  `FrameworkReference Microsoft.AspNetCore.App`) - Minimal API routing does not exist below net8.0, and
  every other package floors at `netstandard2.0`, so this could not live in `Lycia.Extensions` without
  imposing an ASP.NET Core runtime dependency on every consumer. The release workflow's pack loop and
  11-package validation are updated to 12. **`LyciaReliabilitySnapshot` gains `SagaStoreProvider`**
  (the registered SagaStore's provider name outside Split Store). **Fix in the existing diagnostics
  abstraction:** `GetSnapshot()` checked Inbox/Outbox/journal/rebuild registration with
  `GetService<T>() != null`, which for providers whose store factories resolve a connection eagerly (for
  example Redis, via `IConnectionMultiplexer`) actually opened a real connection on every snapshot read -
  registration presence is now checked with `IServiceProviderIsService.IsService(typeof(T))`, which never
  invokes a factory. Verified live on the Microservices Split Store stack (topology reported correctly;
  identical fast `200 OK` with RabbitMQ and Redis both stopped) and with dedicated ASP.NET Core
  `TestServer` tests (mapping/opt-in, snapshot projection across Standard/LocalAtomic/Independent/Split
  Store, secret sentinel values never present in the response, `RequireAuthorization` enforced by standard
  ASP.NET Core authorization, and a timing-based proof no connection is attempted against an unroutable
  Redis address). Feature commit `90ed223`; merged into `dev` as `348288e`. Not released.
- **Post-1.18.0 patch — deferred-fluent CancellationToken ownership and coordinated compensation
  continuation:** `SendWithTracking`/`PublishWithTracking`/`RespondWithTracking`/`ScheduleWithTracking`
  no longer accept a `CancellationToken`; only the terminal `ISagaStepFluent.Then...` call does, and that
  one token now governs the whole deferred operation. The captured/fallback-token machinery
  (`SagaStepFluentToken`) is removed. New API: `Context.ContinueCompensation()` →
  `ICompensationContinuation`/`ICompensatedContinuation`, giving
  `.ThenMarkAsCompensated<TStep>(ct)` (terminal, no propagation) and
  `.ThenMarkAsCompensated<TStep>().ThenBubbleUp(ct)` (terminal, propagates to the logical parent);
  `ContinueCompensation()` never accepts a token and performs no business rollback.
  `ISagaStepFluent.ThenMarkAsCompensated` on the tracked-messaging chain now always calls only
  `MarkAsCompensated` (never auto-bubbles), for consistent naming across both APIs — previously it
  bubbled for coordinated contexts and silently no-opped for reactive ones. Two real bugs fixed: reactive
  `CompensateAndBubbleUp` was a no-op stub (`Task.CompletedTask`) on both the reactive `SagaContext<T>` and
  its step adapter, so every reactive saga's bubble-up silently did nothing; and every handler base
  class's `MarkAsComplete(ct)`/`MarkAsCompensationFailed(ct)` convenience wrapper accepted but discarded
  its token. `ISagaStore.LogStepAsync`/`SaveSagaDataAsync` and `IVersionedSagaStore`'s two methods gained
  an optional `CancellationToken` (previously absent from the interface), threaded through all five
  providers and every context/adapter/coordinator call site. **Known, deliberately unclosed gap** (see
  `HOLD / BACKLOG`): `CompensateParentAsync` persists the child step `Compensated` and then invokes the
  parent handler in-process with no durable "propagation pending" record between the two steps; a crash in
  that window strands propagation, and the same guard that correctly makes ordinary redelivery idempotent
  makes a retry silently skip the parent. `CompensationContinuationTests` reproduces this directly. A real
  fix needs a dedicated durable capability and recovery worker, sized like Outbox/Split Store, not
  attempted here. Full regression green on every provider/TFM including net48; the diagnostics, publisher-
  confirm and Outbox-recovery suites above remain intact (nothing in this change touched transport,
  persistence-provider, Outbox, or Split Store internals — only the saga-context/handler/fluent layer
  above them). Feature commit `5ba04e5`; merged into `dev` as `f67e958`. Not released.
- **Post-1.18.0 patch — durable compensation propagation (closes the bubble-up crash window):** The known
  gap above is closed. `CompensateParentAsync` no longer decides whether to propagate by reading the child
  step's own status (the exact mechanism that stranded propagation on a crash); it durably ensures-and-claims
  a `CompensationPropagationIntent` (identity `SagaId + ChildMessageId`, state machine
  `Pending → Claimed → Completed/Failed`) for the edge and only the claim outcome governs whether the
  immediate in-process attempt runs. Five new methods live directly on `ISagaStore` (not a separate
  interface — propagation durability is `SagaStore` correctness, not an optional add-on), implemented by
  all four built-in providers: InMemory (a locked dictionary), SQL Server / PostgreSQL (a dedicated
  unconditionally-migrated `005_CompensationPropagation` table, the same `ROWLOCK READPAST` / `FOR UPDATE
  SKIP LOCKED` claim pattern already used for Outbox), and Redis (a JSON blob per edge plus one due-time
  ZSET, atomic Lua `EVAL`s). Split Store is a pure pass-through to the canonical store, so it inherits
  canonical durability automatically. `CompensationWorker` (a `BackgroundService`, `OutboxWorker`'s
  shape) is the recovery-only safety net — never the mandatory happy-path executor — registered
  unconditionally by `AddLycia`/`AddLyciaInMemory`, the same precedent `UseSplitStore()` set for
  `ReconciliationWorker`; `LyciaPersistenceBuilder.WithCompensationWorker(...)` only tunes its options. A
  root step creates no propagation intent; propagation follows `ParentMessageId` per edge only, never a
  global scan. Lycia remains at-least-once, never exactly-once, here as everywhere: a parent's compensation
  handler can run more than once for the same logical propagation, and this is documented as the
  compensation handler's own idempotency responsibility, not framework-level exactly-once. Removed
  `CompensateAndBubbleUp<TStep>` entirely (not deprecated) in favor of
  `Context.BubbleUpCompensationAsync<TStep>(ct)`, the primitive `ContinueCompensation()...ThenBubbleUp(ct)`
  already wrapped — same public two/three-stage fluent surface as before, no new concept exposed to
  ordinary handler code. Three real bugs were found and fixed only by writing and running the
  crash-injection and provider-conformance tests against real behavior, not by design review alone: the
  InMemory batch-claim path was checking staleness against the wrong parameter (`leaseDuration` instead of
  `recoveryTimeout`); the Redis batch-claim Lua script reconstructed an edge key using the wrong member
  separator, silently dropping every batch-claimed edge; and a C#-side edge rewrite
  (`MarkCompensationPropagationCompletedAsync`/`FailedAsync`) serialized the status enum as an integer,
  breaking every later Lua string comparison against that edge. `SagaStoreConformanceTests` gained the
  shared compensation-propagation suite (intent creation and its idempotent identity, claim, concurrent
  claim with exactly one winner, stale-claim recovery, completion, retry, attempts exhaustion), run against
  every built-in provider; `CompensationCrashInjectionTests.cs` is new, covering the documented
  crash-window boundaries directly (durable intent before the immediate attempt runs; a stale claim after
  its owner died; bounded retry and terminal exhaustion during the parent's own business compensation;
  idempotent retry after a simulated completion-recording crash; the next edge in a multi-hop chain staying
  durable; cancellation before/after the durable handoff; root and sibling-branching behavior).
  `CompensationContinuationTests`'s former `KnownGap_...` test is now
  `Crash_Simulated_Between_Persisting_Compensated_And_Invoking_The_Parent_No_Longer_Strands_Propagation`,
  proving the same pre-seeded crash state now recovers instead of documenting that it doesn't. Full
  regression green: `Lycia.Tests` (net9.0/net10.0) and `Lycia.Tests.NetFramework` (net48), InMemory/Redis/
  SQL Server/PostgreSQL provider suites (real containers), `Lycia.IntegrationTests` (RabbitMQ
  publisher-confirm, Outbox recovery, saga compensation), `Lycia.Extensions.AspNetCore.Tests`, full
  solution build, `git diff --check`. See DEVELOPERS.md, "Coordinated compensation continuation", for the
  full state machine, persistence authority, worker behavior, and crash-boundary-to-test mapping.
  Feature commit `5a7badb`; merged into `dev` as `30654a6`. Not released.
- **Post-1.18.0 patch — RabbitMQ consumer-readiness CI fix and compensation fluent-API encapsulation:**
  Closes a real GitHub Actions failure introduced by the durable-compensation-propagation merge above:
  `RabbitMqSagaCompensationIntegrationTests.ResponseProducedByOneReplica_IsContinuedByAnotherReplica_FromSharedRedisState`
  failed on net10.0 only (`RabbitMqUnroutableMessageException`, exchange `response.ReplicaResponse`). Root
  cause: `RabbitMqEventBus` declares a response queue/exchange/binding lazily, only when consumption starts
  (`ConsumeWithAckAsync`/`ConsumeAsync`), not during `CreateAsync`; the test started its consumer in an
  unawaited background `Task.Run` and used a fixed `Task.Delay` before publishing, racing that lazy
  declaration under CI's tighter scheduling. This was a test-harness bug, not a production lifecycle bug —
  `RabbitMqEventBus.ConsumerReady` (backed by a `TaskCompletionSource`, already used correctly by
  `RabbitMqEventBusIntegrationTests` via the existing `EventBusReadiness.WaitForConsumersAsync` helper) was
  the deterministic readiness signal the test should have awaited. Both racing call sites in
  `RabbitMqSagaCompensationIntegrationTests.cs` now await it instead. Publisher confirms and
  mandatory/routability checking were not touched or weakened. `.github/workflows/dotnet.yml`'s integration
  job also had its `dotnet test` step split per target framework with distinct trx file names (previously
  net9.0's and net10.0's results shared one file, so the first was silently overwritten — exactly what hid
  the net10.0 failure detail from the uploaded artifact even though the job's own exit code still failed
  correctly); the net10.0 step is guarded with `if: always()` so a net9.0 failure can never suppress it.
  Also closes a real public-API gap the previous phase's final report had flagged:
  `ISagaContext<TInitialMessage>.BubbleUpCompensationAsync<TStep>` was still public and directly callable
  (`Context.BubbleUpCompensationAsync<T>(ct)`), bypassing the intended
  `ContinueCompensation().ThenMarkAsCompensated<T>().ThenBubbleUp(ct)` staged grammar entirely. Removed from
  the public interface; the execution primitive now lives on a new internal `IBubbleUpCompensationPrimitive`
  (`Lycia.Saga.Abstractions.Compensating`) that every built-in saga context implements as an *explicit*
  interface implementation, invisible even on the concrete public `SagaContext<T>` type — this is a
  compile-time impossibility, not a convention. `SagaCompensationContinuation` (the sole intended caller)
  reaches it via a runtime cast that throws a clear `InvalidOperationException` for a hand-written
  `ISagaContext<T>` that doesn't implement it (the one place this invariant is necessarily a runtime check,
  since the public type system cannot express "does this arbitrary external context support bubble-up"
  without exposing the primitive itself). `CoordinatedSagaHandler`/`CoordinatedResponsiveSagaHandler`'s
  default `CompensateAsync` now goes through the same public staged grammar instead of touching the
  primitive. `SendWithTracking`/`PublishWithTracking`/`RespondWithTracking`/`ScheduleWithTracking` and the
  `Then*` fluent surface were audited and found already compliant (no production changes needed there).
  New `FluentApiEncapsulationTests.cs` proves the compiled public surface by reflection. Full regression
  green: `Lycia.Tests` (net9.0/net10.0, 231/231) and `Lycia.Tests.NetFramework` (net48, 46/46),
  `Lycia.Extensions.AspNetCore.Tests`, InMemory/Redis/SQL Server/PostgreSQL provider suites (real
  containers), full solution Debug/Release build, `git diff --check`. `Lycia.IntegrationTests` run six times
  total across both TFMs after the fix (46/46 every time) with no recurrence of the race, plus 8 additional
  isolated net10.0-only runs of the previously-failing test. See DEVELOPERS.md, "The staged-fluent
  encapsulation invariant" (under "Coordinated compensation continuation"). Feature commit `29efc4c`; merged
  into `dev` as `0c07fe0`. Not released.

# FINALIZATION

Milestone: **Persistence / Reliability Architecture**

Status: RELEASED (1.18.0) — milestone closed, no integration pending

`dev` was integrated into `main` and `v1.18.0` was released from it; the checklist below is the historical
record of that integration and is not a standing approval. Work after 1.18.0 (the 1.18.1 patches above) does
not reopen this gate: a later `dev` -> `main` integration requires this section to be deliberately set back
to `Status: READY FOR FINAL INTEGRATION`, with fresh validation evidence for the new release target.

Release target: **1.18.0**, Lycia's first stable release (decided by the repository owner). Tag `v1.18.0`
on the validated `main` merge commit. The pre-existing, unreachable `v1.17.0` tag is left untouched.

Required before `dev` -> `main`:

- Phase 4 Atomic Persistence Boundary — COMPLETE
- Phase 5 Split Store + Reconciliation — COMPLETE
- Phase 6 Canonical Journal + Replay / Rebuild — COMPLETE
- Phase 7 Reliability Hardening — COMPLETE
- Architecture Review — PASS. Whole-system review end to end (transport -> SagaDispatcher -> Inbox ->
  SagaStore -> handler -> Outbox -> session -> broker -> downstream Inbox; Split Store canonical ->
  intent -> journal -> Redis projection; journal -> reducer -> rebuild -> CAS install; scheduling
  claim/lease/fence -> dispatch -> outgoing pipeline). Message-identity, delivery, transaction-boundary,
  Split Store, journal, unknown-commit and public-API invariants hold as documented. Package boundaries
  verified: internal projects stay unpackable and undeclared as dependencies, relational providers embed
  `Lycia.Persistence.Relational.Internal.dll`.
- Reliability Red Team — PASS. Findings and fixes are in `REMEDIATION`.
- Critical Findings — 0 open (1 found and fixed).
- High Findings — 0 open (3 found and fixed: Inbox stale claims, release workflow, handler failures
  invisible in logs and traces).
- Final Regression — PASS, on final `dev` (`925c6d2`): solution builds Release for
  netstandard2.0/net8.0/net9.0/net10.0/net48 with 0 errors and no code warnings (only the test-only
  NU1903 noted in HOLD/BACKLOG); `Lycia.Tests` 179/179 (net9.0, net10.0); `Lycia.Tests.NetFramework`
  46/46 (net48); `Lycia.Persistence.InMemory.Tests` 55/55, Redis 40/40, SQL Server 61/61, PostgreSQL
  61/61 (each on net8.0, net9.0, net10.0, real containers); `Lycia.IntegrationTests` 25/25 (net9.0,
  net10.0); `Lycia.IntegrationTests.NetFramework` 16/16 (net48); `git diff --check` clean.
- Microservices E2E — PASS (live docker compose: RabbitMQ, PostgreSQL, per-service Redis, Jaeger):
  happy path (canonical v5, contiguous journal 1..5, Redis v5, no Inbox duplicates, Outbox
  `ConfirmationUnknown`, reconciliation applied, verify `Healthy`); Redis outage and recovery;
  projection delete + journal rebuild (identical payload, Outbox count unchanged); duplicate delivery;
  Inventory and Payment failure (saga stops at the failed step, warning logged, error span); process
  restart and SIGKILL mid-flight (all sagas complete); RabbitMQ recreate with automatic topology
  recovery and `reset-state.sh rabbitmq`; RabbitMQ Outbox rows reach the attempt cap as
  `ConfirmationUnknown` with no false `Abandoned`.
- Jaeger — PASS: one connected trace per checkout (19 spans, single root, correct parent/child chain
  across all five services through `Outbox.*` producer spans); failed handler spans carry error status
  and `exception.*` tags.
- Package Validation — PASS: CI pack-mode run with a simulated `refs/tags/v1.18.0` build; `nbgv`
  reports `1.18.0`; exactly 11 packages at `1.18.0`, inter-package dependencies pinned `[1.18.0]`,
  expected TFMs (PostgreSQL net8.0+), readme in every package, no internal dependency, relational
  internals embedded.
- Isolated Consumer — PASS: package-only consumers from a clean cache — net10.0 RabbitMQ + PostgreSQL
  (resolves `LocalAtomic`, schema migrated on a real PostgreSQL) and net8.0 Kafka + Redis + InMemory
  Inbox/Outbox + scheduling (resolves `Independent`).
- Documentation — PASS: README.md, DEVELOPERS.md, package READMEs and sample docs audited against the
  final implementation; every C# snippet compiled against the source.
- Release infrastructure — PASS: publishing is tag-driven only (`v*`); NuGet Trusted Publishing via
  GitHub OIDC (`NuGet/login@v1`, policy `lycia-release`, owner `gokayokutucu`, repository `lycia`,
  workflow `dotnet.yml`, no environment); `id-token: write` only on the publish job; no API-key secret
  referenced; tag/version equality and 11-package validation run before any push; workflow passes
  actionlint.

# REMEDIATION (post-Phase-7 review pass)

- **Critical — Outbox dispatch exhaustion silently dropped messages.** A row that exhausted its
  bounded attempts without a broker confirmation stayed at `ConfirmationUnknown` with `RetryCount` at
  the cap, which every provider's claim query filters out, so it was never dispatched again; nothing
  recorded why, and no status or log distinguished it from an in-flight retry. `ConfirmationUnknown`
  was also re-claimable with no staleness gate, so the whole attempt budget burned in roughly four
  seconds of backoff — meaning a broker outage of a few seconds permanently lost outgoing business
  messages, and every message sent through an unconfirming transport such as RabbitMQ was republished
  `MaxAttempts` times in a burst. Reproduced with a deterministic test before fixing. Fixed by gating
  `ConfirmationUnknown` on the recovery window and adding the terminal, explained
  `OutboxMessageStatus.Abandoned` (+ `IOutboxStore.MarkAbandonedAsync`,
  `OutboxDispatchResult.Abandoned`, warning logs naming MessageId/SagaId). Feature commit `4b8bb58`;
  merged as `8a37a10`.
- **High — Inbox claims were never recovered.** A process that died after committing its claim left the
  record in `Processing` for good, and `SagaDispatcher` skips any non-`Started` result and returns
  normally, so the transport acked the message: the work was silently dropped with no dead-letter entry.
  A `Failed` record behaved identically, so a message replayed from a dead-letter queue could never be
  reprocessed, contradicting `InboxBeginResult.AlreadyFailed`'s own documented contract. Exposure is
  limited to topologies where the claim commits independently of the handler transaction (Redis,
  InMemory, mixed relational resolving to `Independent`); under `LocalAtomic` the claim rolls back with
  the handler. Fixed with an atomic, timeout-gated claim takeover in all four providers
  (`InboxOptions.ClaimRecoveryTimeout`, default 5 minutes), keeping `Completed` permanently suppressed.
  Feature commit `a6fc2e3`; merged as `eec1495`.
- **High — the release workflow could not build, and never published the persistence providers.** No job
  installed the .NET 10 SDK although every src project has targeted net10.0 since Phase 7, so
  `dotnet restore Lycia.sln` fails with NETSDK1045 and the publish job's `needs:` could never be
  satisfied; the publish job also never packed or pushed the four `Lycia.Persistence.*` packages, which
  is why none of them has ever appeared on nuget.org even though `AddLycia` refuses to resolve
  `ISagaStore` without one; and the push trigger's `paths` filter also applies to tag pushes, which can
  stop a release tag from starting the workflow at all. Feature commit `cd207d6`; merged as `a4ba23d`.
- **Low — documentation and packaging accuracy.** Outbox lifecycle docs predated `Abandoned`, the Inbox
  claim-recovery window was undocumented, and `Lycia.Persistence.InMemory` was the only public package
  packing without a readme. Feature commit `8afc7ef`; merged as `1527c86`.
- **Refinement of the Critical fix — `Abandoned` only when the final attempt never reached the
  transport.** As first written, the fix also abandoned a final attempt the transport had accepted but
  could not confirm, which for RabbitMQ is every successful publish: every delivered message would have
  been flagged `Abandoned` with an operator-action warning. A final accepted-but-unconfirmed attempt now
  stays `ConfirmationUnknown`; `Abandoned` is reserved for a final attempt that threw or was cancelled.
  Feature commit `aa1e543`; merged as `065e963`.
- **High — handler failures were invisible in logs and traces.** Found during the live E2E: handler base
  classes catch the business exception and record a failed step, so the dispatch returned normally, the
  handler span reported `Completed`, and nothing was logged. The compensation coordinator now logs a
  warning and marks the span as an error with exception tags when it first records a failed step.
  Feature commit `07692b5`; merged as `9fb791e`.
- **Low — documentation.** Documentation release audit: phase history removed from product docs, stale
  "planned"/"not run" claims corrected, a non-compiling README example fixed, Trusted Publishing and the
  release contract documented; CI now also runs on `dev` pushes. Merged as `925c6d2`.
- **Accepted tradeoffs / deferred (not release blockers):** see HOLD/BACKLOG — RabbitMQ publish
  confirmation, the final-attempt crash window, dependency weight, symbol packages, the test-only NU1903
  advisory, and Redis Cluster hash-slot safety.
