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

## Phase 7 — Reliability Hardening

- Address remaining persistence-recovery edge cases, including unknown outcomes and stale ownership
  where still applicable.
- Add leases/fencing only where actually required, plus recovery coordination.
- Harden bounded operational reconciliation and retry behavior.
- Add targeted capability and health reporting where needed, and prepare the production reliability audit.

# NEXT

(No further phases queued behind Phase 7 at this time.)

# HOLD / BACKLOG

- Redis Cluster hash-slot-safe multi-key atomicity unless required by an earlier phase.
- RabbitMQ publisher-confirm capability integration.
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

# FINALIZATION

Milestone: **Persistence / Reliability Architecture**

Status: NOT READY

Required before `dev` -> `main`:

- Phase 4 Atomic Persistence Boundary — COMPLETE
- Phase 5 Split Store + Reconciliation — COMPLETE
- Phase 6 Canonical Journal + Replay / Rebuild — COMPLETE
- Phase 7 Reliability Hardening — ACTIVE
- Full regression validation — PENDING
- Package validation — PENDING
- Documentation validation — PENDING
- Architecture review — PENDING
- Reliability/red-team review — PENDING
- All Critical findings resolved — PENDING
- All High findings resolved or explicitly accepted by the user — PENDING

Only change the status to `READY FOR FINAL INTEGRATION` after every required gate is complete and
recorded. Until then, agents must not merge `dev` into `main`.
