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

# ACTIVE

(No phase currently active. Phase 7 — Reliability Hardening completed below; the milestone now
awaits independent architecture review and the reliability red team before finalization.)

# NEXT

(No further phases queued behind Phase 7 at this time.)

# HOLD / BACKLOG

- Redis Cluster hash-slot-safe multi-key atomicity: inventoried during Phase 7 (Redis Inbox/Outbox
  Lua scripts touch multiple keys — `outbox:msg:{id}`, `outbox:pending` — without hash-tag key naming,
  so cross-key atomicity is not Cluster-safe as written). No real Redis Cluster was available to
  validate a fix, so this stays explicitly on hold rather than being claimed as supported.
- RabbitMQ publisher-confirm capability integration: investigated in Phase 7 (see COMPLETED entry
  below) and deliberately deferred — RabbitMQ.Client 7.1.2 has no supported per-publish confirmation
  await, and a live spike against a real broker hung rather than confirmed. RabbitMQ remains
  `ConfirmationUnknown` rather than a faked confirmation signal.
- Outbox rows stuck at `ConfirmationUnknown` after exhausting `MaxAttempts` currently have no terminal
  state or alerting distinct from an in-flight retry — observed live during the Phase 7 Microservices
  E2E run (harmless to that run's outcome, flagged as a follow-up, not yet implemented).
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
  backoff parity); merged into `dev` as `<pending>`.

# FINALIZATION

Milestone: **Persistence / Reliability Architecture**

Status: NOT READY

Required before `dev` -> `main`:

- Phase 4 Atomic Persistence Boundary — COMPLETE
- Phase 5 Split Store + Reconciliation — COMPLETE
- Phase 6 Canonical Journal + Replay / Rebuild — COMPLETE
- Phase 7 Reliability Hardening — COMPLETE (implementation only; does not itself satisfy the gates below)
- Architecture Review — PENDING
- Reliability Red Team — PENDING
- Critical Findings — PENDING (none raised yet; gate exists to track remediation once red-team runs)
- High Findings — PENDING (none raised yet; gate exists to track remediation once red-team runs)
- Final Regression/Package Validation — PENDING (independent sign-off distinct from this phase's own
  dev-time validation, which is recorded in the Phase 7 COMPLETED entry above)

Only change the status to `READY FOR FINAL INTEGRATION` after every required gate is complete and
recorded. Until then, agents must not merge `dev` into `main`.
