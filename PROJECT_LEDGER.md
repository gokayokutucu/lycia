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

(No phase active. All implementation phases, the architecture review, the reliability red team and
final validation are complete; see `FINALIZATION`.)

# NEXT

(No further phases queued behind Phase 7 at this time.)

# HOLD / BACKLOG

- **PostgreSQL WAL / logical decoding CDC projection feed (future evaluation, not a current-release change or committed roadmap item):** In a PostgreSQL deployment, canonical state changes could be exposed through WAL (*Write-Ahead Log*) logical decoding and CDC (*Change Data Capture*) and consumed to update the Redis operational projection. This would provide a durable feed of **committed** PostgreSQL changes; it would not make PostgreSQL and Redis atomic or eliminate normal replication lag. Redis remains a rebuildable, eventually consistent projection, and its writer must retain version fencing/CAS so delayed or redelivered records cannot overwrite a newer projection. This is distinct from the Journal: `Journal != WAL`; the Journal remains Lycia's framework-level canonical transition history and deterministic rebuild source, while WAL/CDC is only a database change-propagation mechanism. The current provider-neutral `ReconciliationIntent + ReconciliationWorker` remains the default. A possible later boundary is `IProjectionChangeFeed`, with `ReconciliationIntentFeed` and an opt-in PostgreSQL-only `PostgreSqlLogicalReplicationFeed`; only one feed may write a deployment's projection. Candidate consumers include Debezium and a custom logical-replication consumer. Any evaluation must explicitly own replication-slot lag, retained-WAL/disk-pressure, consumer health, LSN progress, recovery, and monitoring. CDC could also be evaluated as an Outbox relay transport (WAL/CDC to broker) while retaining the transactional Outbox record itself and its at-least-once/confirmation semantics. Because Lycia is provider-neutral, this must remain an optional PostgreSQL strategy, never a core requirement.
- Redis Cluster hash-slot-safe multi-key atomicity: inventoried during Phase 7 (Redis Inbox/Outbox
  Lua scripts touch multiple keys — `outbox:msg:{id}`, `outbox:pending` — without hash-tag key naming,
  so cross-key atomicity is not Cluster-safe as written). No real Redis Cluster was available to
  validate a fix, so this stays explicitly on hold rather than being claimed as supported.
- RabbitMQ publish confirmation: deferred. The RabbitMQ transport does not await a per-publish broker
  confirmation, so RabbitMQ Outbox records stay `ConfirmationUnknown` (never a faked `Published`) and
  each message is handed to the broker up to `MaxAttempts` times, spaced by `RecoveryTimeout`; the
  receiving Inbox absorbs the duplicates. This is the current validated behavior, not a fixed
  architectural limit, and may be revisited.
- Outbox crash during the final attempt (Medium): `MarkPublishingAsync` increments `RetryCount`, so a
  process crash after the final attempt is marked `Publishing` but before its outcome is recorded leaves
  the row `Publishing` at the attempt cap, which no claim query returns. Requires a crash in exactly that
  window after all earlier attempts were used. A fix would terminalize stale `Publishing` rows at the cap
  (for example by making the claim query mark them `Abandoned`).
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

# FINALIZATION

Milestone: **Persistence / Reliability Architecture**

Status: READY FOR FINAL INTEGRATION

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
