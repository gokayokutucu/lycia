# PROJECT LEDGER

Milestone: **Persistence / Reliability Architecture**

This ledger records phase order and final-integration authority. `AGENTS.md` defines the operational
Git rules. Agents must update this file as phases move through the milestone.

# LOCKED

- Normal phase work starts from the latest local `dev` and merges back only to `dev`.
- `main` is updated only at an authorized, fully validated milestone boundary.
- Messaging remains transport-independent and at least once; do not claim exactly-once delivery.
- Scheduling owns delayed intent until due, then hands the original Send/Publish/Respond semantic to
  the outgoing pipeline. Do not add a competing Outbox timer record.
- Atomic SagaStore + Inbox + Outbox behavior must be honest about provider capabilities. Do not
  represent InMemory or Redis as sharing a relational transaction.
- Stable message identities and Inbox idempotency are the duplicate-delivery safety boundary.

# ACTIVE

## Phase 4 — Strong Consistency / Atomic Persistence Boundary

Wire relational SagaStore, Inbox, and Outbox operations into the existing
`ILyciaPersistenceSession` transaction boundary. Preserve explicit non-atomic behavior for providers
that cannot supply the same guarantee.

# NEXT

1. **Phase 5 — Split Store:** define and implement the supported Redis + relational ownership model.
2. **Phase 6 — Deterministic Replay/Rebuild:** introduce canonical history and deterministic rebuild
   semantics without treating broker delivery as a journal.
3. **Phase 7 — Recovery and Coordination:** add bounded reconciliation/recovery and leases or fencing
   where ownership can become stale.

# HOLD / BACKLOG

- Redis Cluster hash-slot-safe multi-key atomicity.
- RabbitMQ publisher-confirm capability integration.
- Persistence capability reporting and health checks beyond the current SagaStore health contract.
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
- **Phase 3 — Outbox message pipeline:** automatic Send/Publish/Respond capture, semantic dispatch,
  hosted bounded-retry worker, scheduling handoff, honest transport confirmations, and provider
  recovery tests. Feature commit `4939c70`; merged into `dev` as `40a6811`.

# FINALIZATION

Milestone: **Persistence / Reliability Architecture**

Status: NOT READY

Required before `dev` -> `main`:

- Phase 4 Strong Consistency / Atomic Persistence Boundary — ACTIVE
- Phase 5 Split Store — PENDING
- Phase 6 Deterministic Replay/Rebuild — PENDING
- Phase 7 Recovery and Coordination — PENDING
- Full regression, package, and documentation validation — PENDING
- Architecture review — PENDING
- Reliability Red Team — PENDING
- Resolution of all Critical and High reliability findings — PENDING

Only change the status to `READY FOR FINAL INTEGRATION` after every required gate is complete and
recorded. Until then, agents must not merge `dev` into `main`.
