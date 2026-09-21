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
- ~~Outbox rows stuck at `ConfirmationUnknown` after exhausting `MaxAttempts`~~ — RESOLVED in the
  post-Phase-7 review pass. The red team found this was not harmless: it silently dropped outgoing
  messages after a broker outage of a few seconds. See `REMEDIATION` under `FINALIZATION`.
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

# FINALIZATION

Milestone: **Persistence / Reliability Architecture**

Status: NOT READY

Required before `dev` -> `main`:

- Phase 4 Atomic Persistence Boundary — COMPLETE
- Phase 5 Split Store + Reconciliation — COMPLETE
- Phase 6 Canonical Journal + Replay / Rebuild — COMPLETE
- Phase 7 Reliability Hardening — COMPLETE (implementation only; does not itself satisfy the gates below)
- Architecture Review — PASS. Whole-system review end to end (transport -> SagaDispatcher -> Inbox ->
  SagaStore -> handler -> Outbox -> session -> broker -> downstream Inbox; Split Store canonical ->
  intent -> journal -> Redis projection; journal -> reducer -> rebuild -> CAS install; scheduling
  claim/lease/fence -> dispatch -> outgoing pipeline; HTTP -> handler -> Outbox -> RabbitMQ ->
  downstream -> continuation). Message-identity, delivery, transaction-boundary, Split Store, journal,
  unknown-commit and public-API invariants hold as documented. Package boundaries verified: the four
  internal support projects stay unpackable, no public package declares a dependency on them, the
  relational providers embed `Lycia.Persistence.Relational.Internal.dll`, and provider dependency
  closure is clean (PostgreSQL pulls only Npgsql, etc.).
- Reliability Red Team — PASS. One Critical and one High product finding, both reproduced before being
  fixed, plus three release-infrastructure findings. All are closed; see `REMEDIATION` below.
- Critical Findings — 0 open (1 found, fixed in `4b8bb58`).
- High Findings — 0 open (1 product finding fixed in `a6fc2e3`; release-workflow findings fixed in
  `cd207d6`).
- Final Regression/Package Validation — **INCOMPLETE — BLOCKED**. What passed: `dotnet restore`, Debug
  and Release builds of the whole solution (netstandard2.0/net8.0/net9.0/net10.0/net48, 0 errors);
  `Lycia.Tests` 175/175 of the non-container tests on both net9.0 and net10.0;
  `Lycia.Persistence.InMemory.Tests` 54/54 on net8.0, net9.0 and net10.0; all eleven public packages
  packed in CI pack mode and inspected (id/version/authors/licence/readme/repository/TFMs/dependencies);
  and the mandatory isolated-consumer test passed for five package-only combinations, including one on
  net10.0 and one mixed-provider topology. What could NOT be executed: every container-dependent suite,
  because the local Docker engine stopped responding partway through this pass and did not recover
  (`DockerUnavailableException`). That leaves unrun: the Redis, SQL Server and PostgreSQL provider
  suites (which are the only coverage of the new Inbox takeover SQL/Lua and the Outbox claim predicates
  on real engines), `Lycia.IntegrationTests`, 2 Testcontainers tests in `Lycia.Tests`, 14 in
  `Lycia.Tests.NetFramework`, and the whole Microservices docker-compose E2E including the Jaeger
  trace re-verification.

Remaining blockers before `dev` -> `main` and before any release tag:

1. The container-dependent regression above must actually run and pass on a host with a working Docker
   engine. The Inbox and Outbox remediations change provider SQL and a Redis Lua script, so the
   per-provider conformance runs are not optional confirmation.
2. The release version is genuinely ambiguous and needs a human decision, so no tag was created. Every
   one of the 24 versions published to nuget.org is a prerelease of the form `1.17.0-beta-<height>-g<sha>`;
   there has never been a stable release. `version.json` pins the base to `1.17-beta.{height}`, so a tag
   does not determine the package version: tagging anything matching `v*.*.*` today would publish
   `1.17.0-beta-0188`-style prereleases regardless of the tag's name, which would make the tag name and
   the shipped version disagree. The one existing tag, `v1.17.0`, is also unreachable from both `main`
   and `dev` (it predates the history rewrite recorded by the `backup/pre-ai-attribution-cleanup-*`
   branches) and must not be moved or deleted. Deciding between "publish the next beta under the current
   scheme" and "make this milestone the first stable release, which requires editing `version.json`" is
   a release-policy call, not something to infer.

Only change the status to `READY FOR FINAL INTEGRATION` after every required gate is complete and
recorded. Until then, agents must not merge `dev` into `main`.

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
- **Accepted tradeoffs / deferred (not release blockers).** RabbitMQ still reports
  `ConfirmationUnknown` because RabbitMQ.Client exposes no supported per-publish confirmation await;
  this is now operationally visible through `Abandoned` rather than silent. `Lycia.Extensions` imposes
  Autofac, Serilog and Apache.Avro on every consumer transitively, which is dependency bloat worth
  revisiting but not a correctness issue. Redis Cluster hash-slot safety remains on hold as recorded
  above.
