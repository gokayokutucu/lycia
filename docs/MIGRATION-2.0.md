# Migrating from Lycia 1.18.x to 2.0.x

Lycia 2.0.0 intentionally contains source-breaking public API changes over `1.18.0`. This is the
major-version boundary where the API is cleaned up rather than carrying every 1.x shape forward; every
break below is deliberate and every one of them is documented. Lycia remains at-least-once, never
exactly-once, in 2.0.x exactly as it was in 1.18.0 - that guarantee has not changed.

If something you rely on isn't covered here, it almost certainly didn't change: this guide only lists
actual differences from `1.18.0`, verified against the source.

> [!IMPORTANT]
> **If you are migrating (or upgrading) to `2.0.1` or later, read [Update: Lycia 2.0.1 simplifies
> compensation further](#update-lycia-201-simplifies-compensation-further) at the bottom of this guide
> first.** `2.0.1` is an immediate correction release: it removes the `ContinueCompensation()`/
> `ICompensationContinuation` staged entry point, `Context.Compensate(...)`, and the advanced imperative
> `Context.BubbleUpCompensation(...)` that sections 3, 4 and 6 below describe - all three existed only in
> `2.0.0` and are gone again in `2.0.1`. The `2.0.0` packages are unlisted on NuGet.org once `2.0.1` is
> confirmed available, so a reader migrating from `1.18.x` today should go directly to the `2.0.1` grammar
> in that section rather than adopting the intermediate `2.0.0`-only shapes below. Sections 1, 2, 5, 7 and 8
> are unaffected and remain accurate for `2.0.1`.

## Source-breaking changes

### 1. `*WithTracking` starter methods no longer accept a `CancellationToken`

`SendWithTracking`, `PublishWithTracking`, `RespondWithTracking`, and `ScheduleWithTracking` moved their
`CancellationToken` parameter to the terminal `Then...` call. A deferred tracked operation now has exactly
one token, owned by whichever `Then...` method you await - not two, and not an implicit fallback between
them.

```csharp
// 1.18.x
await Context
    .SendWithTracking(command, cancellationToken)
    .ThenMarkAsComplete();

// 2.0.0
await Context
    .SendWithTracking(command)
    .ThenMarkAsComplete(cancellationToken);
```

This applies identically to `Publish`, `Respond`, and `Schedule`:

```csharp
await Context.PublishWithTracking(nextEvent).ThenMarkAsComplete(cancellationToken);
await Context.RespondWithTracking(request, response).ThenMarkAsComplete(cancellationToken);
await Context.ScheduleWithTracking(message, delay).ThenMarkAsComplete(cancellationToken);
```

The starter method itself still does nothing until a terminal `Then...` method is awaited - that was
already true in `1.18.0` and remains true.

### 2. `Context.CompensateAndBubbleUp<TStep>(cancellationToken)` is removed

There is no compatibility shim; it does not compile in 2.0.0. It has two replacements, depending on how
much control you need - see "Compensation API changes" below.

### 3. Coordinated compensation is now a staged, recommended fluent grammar

**Recommended** (equivalent to the old `CompensateAndBubbleUp<TStep>(ct)`, but now compile-time staged and
crash-recoverable):

```csharp
await Context
    .ContinueCompensation()
    .ThenMarkAsCompensated<TStep>()
    .ThenBubbleUp(cancellationToken);
```

**Root/final step** (stops without propagating - same call as before, unchanged):

```csharp
await Context.MarkAsCompensated<TStep>(cancellationToken);
```

**Advanced explicit alternative**, if you need imperative step-by-step control (new in 2.0.0 - see
below):

```csharp
await Context.MarkAsCompensated<TStep>(cancellationToken);
await Context.BubbleUpCompensation(failedEvent, cancellationToken);
```

`Context.Compensate(failedEvent, cancellationToken)` is unchanged and is a *separate* operation - a
reactive compensation-event trigger for choreography, not a required first step of coordinated bubble-up.
Do not assume it is part of every migration; only touch call sites that actually used the removed
`CompensateAndBubbleUp`.

### 4. The old public `BubbleUpCompensationAsync` escape hatch is gone; a new, safer, explicit one replaces it

A previous 2.0-track change removed `ISagaContext<TInitialMessage>.BubbleUpCompensationAsync<TStep>(ct)` -
a public method that let application code call the fluent form's internal execution primitive directly,
bypassing the staged grammar (`Context.BubbleUpCompensationAsync<T>(ct)` no longer compiles). That removal
is final; it is not resurrected in 2.0.0.

2.0.0 instead introduces a new, deliberately different, explicit advanced API for the same underlying
need - imperative compensation control:

```csharp
Task BubbleUpCompensation<TStep>(TStep failedEvent, CancellationToken cancellationToken = default)
    where TStep : IMessage;
```

Do not confuse the two. The old one took no message argument and let the framework guess "the current
step" from the injected context; the new one requires you to pass the exact message explicitly
(`failedEvent`), and Lycia validates that message's identity against the step the context was constructed
for, and validates that the step is already `Compensated`, before doing anything durable - see
`DEVELOPERS.md`, "Advanced imperative compensation API", for the full validation contract and why it
exists. Passing the wrong message (a sibling step, an unrelated message, or a message of the same type
from a different step) throws `InvalidOperationException` rather than silently propagating the wrong edge.

**If you omit `BubbleUpCompensation` after `MarkAsCompensated`, Lycia cannot infer that parent propagation
was intended** - `MarkAsCompensated` alone is also a fully valid, terminal root/final compensation call.
This is exactly why the recommended form is the staged fluent grammar, not this one: forgetting the second
call there simply won't compile in a form that silently drops propagation.

### 5. `ISagaStore` gained five compensation-propagation methods

Custom `ISagaStore` implementations must now also implement:

```csharp
Task<CompensationPropagationClaim> EnsureAndClaimCompensationPropagationAsync(...);
Task<IReadOnlyList<CompensationPropagationIntent>> ClaimDueCompensationPropagationsAsync(...);
Task MarkCompensationPropagationCompletedAsync(...);
Task MarkCompensationPropagationFailedAsync(...);
Task<CompensationPropagationIntent?> GetCompensationPropagationIntentAsync(...);
```

This is deliberate: compensation propagation durability is part of `SagaStore` correctness in 2.0.0, not
an optional add-on. If you have a custom `ISagaStore` provider, see `DEVELOPERS.md`, "Persistence
authority: `ISagaStore`, not a second store", for the exact contract each method must honor (in
particular: identity is `SagaId` + `ChildMessageId`, and duplicate creation for the same edge must be
idempotent, not a new logical intent). All four built-in providers (InMemory, Redis, SQL Server,
PostgreSQL) already implement this; Split Store delegates to its wrapped canonical provider automatically.

### 6. `ISagaCompensationCoordinator` gained `BubbleUpCompensationAsync`

A custom `ISagaCompensationCoordinator` implementation (uncommon - this is internal coordination
machinery, not a typical extension point) must now also implement:

```csharp
Task BubbleUpCompensationAsync(Guid sagaId, Type stepType, Type handlerType, IMessage currentStep,
    IMessage failedEvent, CancellationToken cancellationToken = default);
```

### 7. `IRequestRoutingMetadata.ReplyTo` is removed

`ReplyTo` was an obsolete compatibility alias for `ResponseEndpoint`, marked in `1.18.0` as "will be
removed in a future major version" - this is that version. Use `ResponseEndpoint` directly; it has carried
the same value since `1.18.0`. `CommandBase`/`ResponseBase` no longer implement `ReplyTo`.

### 8. `LyciaSchedulingBuilder.WithWorker(...)` is removed

Obsolete since it exposed the internal `SchedulerWorker` implementation detail in its name. Use
`WithDispatch(...)` - it configures the exact same `SchedulerWorkerOptions`, with no behavior difference.

```csharp
// 1.18.x (obsolete, still compiled)
builder.AddScheduling().WithWorker(w => w.BatchSize = 10);

// 2.0.0
builder.AddScheduling().WithDispatch(w => w.BatchSize = 10);
```

## Additive changes (non-breaking)

- **`Lycia.Extensions.AspNetCore`** - new package, opt-in `app.MapLyciaDiagnostics()` Minimal API endpoint
  exposing the reliability/persistence topology snapshot as JSON. Not required; existing applications are
  unaffected unless they reference it.
- **Durable compensation propagation** - `ThenBubbleUp`/`BubbleUpCompensation` are now recoverable after a
  process crash: a durable `CompensationPropagationIntent` per edge, a hosted `CompensationWorker` safety
  net, bounded retries, and an operator-visible terminal `Failed` state after exhaustion. You do not call
  anything extra to opt into this; it is part of `SagaStore` correctness. See `DEVELOPERS.md`, "Coordinated
  compensation continuation", for the full architecture.
- **RabbitMQ publisher confirms** - outgoing RabbitMQ publishes now wait for broker confirmation and report
  an unroutable `mandatory` publish as `RabbitMqUnroutableMessageException` instead of succeeding silently.
- **Outbox final-attempt crash recovery** - an Outbox message whose last permitted attempt's outcome was
  lost to a worker crash gets one additional recovery attempt before being marked `Abandoned`.

## Behavioral changes worth knowing about (not API-breaking)

- **Compensation propagation is at-least-once, never exactly-once** - a parent's compensation handler can
  run more than once if a worker crashes between invoking it and recording completion. This was already
  true of every other handler invocation in Lycia; it now also applies explicitly to bubble-up recovery.
  Make external side effects in compensation handlers idempotent.
- **Terminal `CancellationToken` semantics apply to the whole deferred/composite operation** - for both
  `*WithTracking` and `ContinueCompensation()...ThenBubbleUp(ct)`, the single terminal token governs
  everything from the deferred operation through the framework state transition that follows it, not just
  the last step.

## Not part of this migration

The following are explicitly **not** 2.0.0 work and are unaffected by this release:

- Queue / Stream / PubSub messaging-semantics redesign (the "Messaging Semantics Architecture Spike" -
  tracked separately, post-2.0).
- RabbitMQ Streams and Super Streams, Kafka Share Groups (KIP-932) - not implemented in 1.18.0, still not
  implemented in 2.0.0.
- Supported infrastructure version floors/ceilings - unchanged from `1.18.0` (see `infrastructure-versions.json`
  and `DEVELOPERS.md`, "Supported infrastructure versions").

## Update: Lycia 2.0.1 simplifies compensation further

`2.0.1` is an immediate correction release over `2.0.0` (not a new major version): it collapses the
`2.0.0` compensation API described in sections 2, 3, 4 and 6 above into a simpler, two-stage form, and
removes `Context.Compensate(...)` entirely. Everything else in this guide (sections 1, 5, 7, 8, and the
additive/behavioral-change lists) is unchanged and still describes `2.0.1` accurately.

**Coordinated compensation** is now exactly two call shapes, both directly on `ISagaContext` - there is no
`ContinueCompensation()` entry point and no separate continuation interface for the two-stage terminal form:

```csharp
// Intermediate step - marks compensated, then propagates to the logical parent, atomically:
await Context
    .MarkAsCompensated<TStep>()
    .ThenBubbleUp(cancellationToken);

// Root/final step - marks compensated and stops there (unchanged since 1.18.0):
await Context.MarkAsCompensated<TStep>(cancellationToken);
```

If you already migrated to `2.0.0`'s three-stage form, replace it directly:

```csharp
// 2.0.0
await Context
    .ContinueCompensation()
    .ThenMarkAsCompensated<TStep>()
    .ThenBubbleUp(cancellationToken);

// 2.0.1
await Context
    .MarkAsCompensated<TStep>()
    .ThenBubbleUp(cancellationToken);
```

**Removed entirely, not obsolete, does not compile in `2.0.1`:**

- `Context.ContinueCompensation()` and `ICompensationContinuation` (`Lycia.Saga.Abstractions.Compensating`) -
  the no-token `MarkAsCompensated<TStep>()` overload on `ISagaContext` is the new, sole staging entry
  point, and it returns `ICompensatedContinuation` (kept, unchanged) directly.
- `Context.Compensate<T>(T @event, CancellationToken ct) where T : IFailedEventBase` - the advanced
  imperative `2.0.0` API added specifically to back the now-removed `BubbleUpCompensation`. If you used
  `Context.Compensate(...)` for reactive/choreography compensation, use `Context.Publish(failedEvent, ct)`
  instead - it is the exact same publish `Compensate` performed internally, since `Compensate` never did
  anything beyond that publish. See `DEVELOPERS.md`, "Reactive/choreography compensation:
  `Context.Publish(failedEvent, ct)`".
- `Context.BubbleUpCompensation<TStep>(TStep failedEvent, CancellationToken ct)` (the `2.0.0` advanced
  imperative primitive) and `ISagaCompensationCoordinator.BubbleUpCompensationAsync(...)` (its backing
  coordinator method, from section 6 above) - both existed only to back the two-call imperative form; the
  staged `MarkAsCompensated<TStep>().ThenBubbleUp(ct)` form is now the only application-facing entry point
  to parent-lineage propagation, matching the original `2.0.0` recommendation, not a new restriction.

**Unaffected by this correction:** the durable propagation machinery itself
(`CompensationPropagationIntent`, `CompensationWorker`, at-least-once recovery semantics, exact `MessageId`/
`ParentMessageId`-based identity, sibling isolation) - `2.0.1` routes through the exact same
`SagaCompensationCoordinator.CompensateParentAsync`/`EnsureClaimAndAttemptPropagationAsync` implementation
`2.0.0`'s staged fluent form already used. This is an API-surface simplification, not a reliability change.

**Bug fix verified during this correction:** the reactive dispatcher's failed-event routing
(`SagaDispatcher.FindMethodName`) now recognizes any `IFailedEventBase` implementer, not only the concrete
`Lycia.Saga.Messaging.FailedEventBase` base class - previously, an event implementing `IFailedEventBase`
directly (without deriving from `FailedEventBase`) would not have dispatched to `CompensateAsync` even
though `Context.Compensate<T>`'s generic constraint in `2.0.0` suggested it should. This only affects
custom failed-event types that implement `IFailedEventBase` directly; every event deriving from the
concrete `FailedEventBase` class dispatched correctly both before and after this fix.

## Getting help

If an upgrade issue isn't covered above, check `DEVELOPERS.md` first - it documents the current
architecture in depth, including the compensation state machine, persistence provider contracts, and the
release process. If you believe a change here is missing or inaccurate against the actual `2.0.x` source,
open an issue.
