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
- Split Store canonical ownership belongs to the later Split Store phase.
- Replay/rebuild must be deterministic and must not invoke business handlers.

# ACTIVE

## Phase 4 — Atomic Persistence Boundary

- Resolve the persistence topology automatically with default `Auto` behavior.
- Provide `RequireAtomicBoundary()` and `UseIndependentTransactions()` as the explicit overrides.
- Share one local transaction for SQL Server and PostgreSQL when the participating stores are compatible.
- Make the service-local Inbox + SagaStore + Outbox boundary atomic where that local transaction is available.
- Do not introduce cross-service transactions or an exactly-once delivery claim.

# NEXT

1. **Phase 5 — Split Store + Reconciliation**
   - Define the supported Redis operational/materialized-state and relational canonical-persistence model.
   - Remove unsafe request-path dual-write assumptions.
   - Commit relational canonical state first, then reconcile into Redis in a controlled way.
   - Make operational-state and canonical-state ownership explicit.
   - Do not implement replay beyond what the split-store design requires.
2. **Phase 6 — Canonical Journal + Replay / Rebuild**
   - Introduce a canonical immutable transition/history model with deterministic per-saga ordering.
   - Define a deterministic reducer and replay/rebuild that does not invoke business handlers.
   - Rebuild Redis/materialized state while preserving message identity and saga-version semantics.
   - Broker delivery is not the canonical journal.
3. **Phase 7 — Reliability Hardening**
   - Address remaining persistence-recovery edge cases, including unknown outcomes and stale ownership
     where still applicable.
   - Add leases/fencing only where actually required, plus recovery coordination.
   - Harden bounded operational reconciliation and retry behavior.
   - Add targeted capability and health reporting where needed, and prepare the production reliability audit.

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

# FINALIZATION

Milestone: **Persistence / Reliability Architecture**

Status: NOT READY

Required before `dev` -> `main`:

- Phase 4 Atomic Persistence Boundary — PENDING
- Phase 5 Split Store + Reconciliation — PENDING
- Phase 6 Canonical Journal + Replay / Rebuild — PENDING
- Phase 7 Reliability Hardening — PENDING
- Full regression validation — PENDING
- Package validation — PENDING
- Documentation validation — PENDING
- Architecture review — PENDING
- Reliability/red-team review — PENDING
- All Critical findings resolved — PENDING
- All High findings resolved or explicitly accepted by the user — PENDING

Only change the status to `READY FOR FINAL INTEGRATION` after every required gate is complete and
recorded. Until then, agents must not merge `dev` into `main`.
