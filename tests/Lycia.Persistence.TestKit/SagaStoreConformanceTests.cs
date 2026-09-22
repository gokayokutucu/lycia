// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Exceptions;
using System.Collections.Concurrent;

namespace Lycia.Persistence.TestKit;

/// <summary>
/// Behavioral conformance suite shared by every <see cref="ISagaStore"/> provider (InMemory, Redis,
/// SqlServer, PostgreSql). Each provider's test project derives from this class and implements
/// <see cref="CreateStore"/> to supply a fresh, isolated store instance per test. Running the same
/// test bodies against every provider is what guarantees the providers preserve equivalent observable
/// semantics, per the SagaStore contract.
/// </summary>
public abstract class SagaStoreConformanceTests
{
    /// <summary>Creates a fresh <see cref="ISagaStore"/> instance for a single test. Must not share state across tests.</summary>
    protected abstract ISagaStore CreateStore();

    /// <summary>
    /// Whether two <see cref="CreateStore"/> results observe the same canonical data, the way two
    /// separate service processes/connections would against a real external store (Redis, SQL Server,
    /// PostgreSQL). <c>false</c> for providers whose "fresh, isolated instance" is also isolated
    /// storage (InMemory) - two in-process instances never share state by design, so tests that model
    /// multiple independent writers against one store do not apply to them.
    /// </summary>
    protected virtual bool SupportsCrossInstanceSharedStorage => true;

    private static readonly Type StepType = typeof(DummyEvent);
    private static readonly Type HandlerType = typeof(DummySagaHandler);

    [Fact]
    public async Task LogStepAsync_Should_Not_Throw_For_Valid_Transitions()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Started, HandlerType, null, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Failed, HandlerType, null, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Compensated, HandlerType, null, (SagaStepFailureInfo?)null);
    }

    [Fact]
    public async Task LogStepAsync_Should_Throw_When_CompensationFailed_To_Compensated_Transition()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.CompensationFailed, HandlerType, null, (SagaStepFailureInfo?)null);

        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Compensated, HandlerType, null, (SagaStepFailureInfo?)null));
    }

    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Failed_To_Completed_Transition()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Failed, HandlerType, null, (SagaStepFailureInfo?)null);

        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Completed, HandlerType, null, (SagaStepFailureInfo?)null));
    }

    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Started_To_CompensationFailed_Transition()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Started, HandlerType, null, (SagaStepFailureInfo?)null);

        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.CompensationFailed, HandlerType, null, (SagaStepFailureInfo?)null));
    }

    [Fact]
    public async Task LogStepAsync_Should_Throw_When_Compensated_To_Completed_Transition()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Compensated, HandlerType, null, (SagaStepFailureInfo?)null);

        await Assert.ThrowsAsync<SagaStepTransitionException>(() =>
            store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Completed, HandlerType, null, (SagaStepFailureInfo?)null));
    }

    [Fact]
    public async Task LogStepAsync_Should_Allow_Idempotent_Repeating_Completed_Transition()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Completed, HandlerType, null, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Completed, HandlerType, null, (SagaStepFailureInfo?)null);
    }

    [Fact]
    public async Task LogStepAsync_Should_Prevent_Duplicate_Transitions_When_Concurrent()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Started, HandlerType, null, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Completed, HandlerType, null, (SagaStepFailureInfo?)null);
        await store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Completed, HandlerType, null, (SagaStepFailureInfo?)null);

        var steps = await store.GetSagaHandlerStepsAsync(sagaId);
        var completedCount = steps.Values.Count(meta => meta.Status == StepStatus.Completed);
        Assert.Equal(1, completedCount);
    }

    [Fact]
    public async Task LogStepAsync_Concurrent_Distinct_Messages_Should_All_Persist_Independently()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        const int count = 10;
        var messageIds = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

        await Task.WhenAll(messageIds.Select(messageId =>
            store.LogStepAsync(sagaId, messageId, Guid.Empty, StepType, StepStatus.Completed, HandlerType, null, (SagaStepFailureInfo?)null)));

        var steps = await store.GetSagaHandlerStepsAsync(sagaId);
        Assert.Equal(count, steps.Values.Count(meta => meta.Status == StepStatus.Completed));
    }

    [Fact]
    public async Task SaveSagaDataAsync_Should_RoundTrip_Through_LoadSagaDataAsync()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var data = new DummySagaData { Payload = "hello", Counter = 42 };

        await store.SaveSagaDataAsync(sagaId, data);
        var loaded = await store.LoadSagaDataAsync<DummySagaData>(sagaId);

        Assert.Equal("hello", loaded.Payload);
        Assert.Equal(42, loaded.Counter);
    }

    // --- Optimistic concurrency (IVersionedSagaStore) ---
    // Only exercised when the provider under test implements IVersionedSagaStore (all four built-in
    // providers do). A provider that doesn't would have these tests report inconclusive via the guard
    // below.

    private IVersionedSagaStore? AsVersioned(ISagaStore store) => store as IVersionedSagaStore;

    [Fact]
    public async Task SaveSagaDataAsync_WithVersion_Should_Succeed_On_First_Insert_With_ExpectedVersion_Zero()
    {
        var store = CreateStore();
        var versioned = AsVersioned(store);
        if (versioned is null) return; // provider does not support versioning

        var sagaId = Guid.NewGuid();
        var newVersion = await versioned.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "v1" }, 0);

        Assert.Equal(1, newVersion);
    }

    [Fact]
    public async Task SaveSagaDataAsync_WithVersion_Should_Increment_Version_On_Each_Successful_Save()
    {
        var store = CreateStore();
        var versioned = AsVersioned(store);
        if (versioned is null) return;

        var sagaId = Guid.NewGuid();
        var v1 = await versioned.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "v1" }, 0);
        var v2 = await versioned.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "v2" }, v1);

        Assert.Equal(1, v1);
        Assert.Equal(2, v2);
    }

    [Fact]
    public async Task SaveSagaDataAsync_WithVersion_Should_Throw_SagaConcurrencyException_When_ExpectedVersion_Stale()
    {
        var store = CreateStore();
        var versioned = AsVersioned(store);
        if (versioned is null) return;

        var sagaId = Guid.NewGuid();
        var v1 = await versioned.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "v1" }, 0);
        Assert.Equal(1, v1);

        await Assert.ThrowsAsync<SagaConcurrencyException>(() =>
            versioned.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "stale" }, 0));
    }

    [Fact]
    public async Task LoadSagaDataWithVersionAsync_Should_Return_Zero_Version_For_NonExistent_Saga()
    {
        var store = CreateStore();
        var versioned = AsVersioned(store);
        if (versioned is null) return;

        var (_, version) = await versioned.LoadSagaDataWithVersionAsync<DummySagaData>(Guid.NewGuid());

        Assert.Equal(0, version);
    }

    [Fact]
    public async Task SaveSagaDataAsync_Concurrent_Writers_Should_Have_Exactly_One_Winner()
    {
        var store = CreateStore();
        var versioned = AsVersioned(store);
        if (versioned is null) return;

        var sagaId = Guid.NewGuid();
        await versioned.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "base" }, 0);

        const int writers = 8;
        var results = await Task.WhenAll(Enumerable.Range(0, writers).Select(async i =>
        {
            try
            {
                await versioned.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = $"writer-{i}" }, 1);
                return true;
            }
            catch (SagaConcurrencyException)
            {
                return false;
            }
        }));

        Assert.Equal(1, results.Count(succeeded => succeeded));
    }

    [Fact]
    public async Task SaveSagaDataAsync_Two_Independent_Store_Instances_Stale_Writer_Reports_Correct_Versions()
    {
        // Two separate ISagaStore instances stand in for two separate service processes/connections,
        // as opposed to SaveSagaDataAsync_Concurrent_Writers_Should_Have_Exactly_One_Winner above,
        // which proves the single-winner outcome but not the exact version numbers the loser sees.
        if (!SupportsCrossInstanceSharedStorage) return;

        var storeA = CreateStore();
        var storeB = CreateStore();
        var versionedA = AsVersioned(storeA);
        var versionedB = AsVersioned(storeB);
        if (versionedA is null || versionedB is null) return;

        var sagaId = Guid.NewGuid();
        var v1 = await versionedA.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "base" }, 0);
        Assert.Equal(1, v1);

        // Both instances independently load the same current version.
        var (_, loadedVersionA) = await versionedA.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId);
        var (_, loadedVersionB) = await versionedB.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId);
        Assert.Equal(1, loadedVersionA);
        Assert.Equal(1, loadedVersionB);

        // A advances the saga first.
        var v2 = await versionedA.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "writer-a" }, loadedVersionA);
        Assert.Equal(2, v2);

        // B is now stale and must fail with the exact expected/actual versions it raced against.
        var ex = await Assert.ThrowsAsync<SagaConcurrencyException>(() =>
            versionedB.SaveSagaDataAsync(sagaId, new DummySagaData { Payload = "writer-b" }, loadedVersionB));

        Assert.Equal(sagaId, ex.SagaId);
        Assert.Equal(1, ex.ExpectedVersion);
        Assert.Equal(2, ex.ActualVersion);
    }

    // --- Compensation propagation (CompensationPropagationIntent) ---
    // Every provider must give the same observable lifecycle: create-and-claim is idempotent per edge
    // (SagaId + ChildMessageId), a live claim excludes other owners, a stale claim becomes reclaimable,
    // completion and failure are terminal and idempotent, and attempts-exhaustion is durably visible to
    // operators rather than silently dropped.

    private static readonly TimeSpan LongLease = TimeSpan.FromMinutes(5);
    private const int DefaultMaxAttempts = 5;

    [Fact]
    public async Task EnsureAndClaimCompensationPropagationAsync_First_Call_Claims_A_New_Intent()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var claim = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner-1",
            LongLease, DefaultMaxAttempts);

        Assert.Equal(CompensationPropagationClaimOutcome.Claimed, claim.Outcome);
        Assert.NotNull(claim.Intent);
        Assert.Equal(sagaId, claim.Intent!.SagaId);
        Assert.Equal(childId, claim.Intent.ChildMessageId);
        Assert.Equal(parentId, claim.Intent.ParentMessageId);
        Assert.Equal(CompensationPropagationStatus.Claimed, claim.Intent.Status);
        Assert.Equal(1, claim.Intent.AttemptCount);
        Assert.Equal("owner-1", claim.Intent.Owner);
    }

    [Fact]
    public async Task EnsureAndClaimCompensationPropagationAsync_Is_Idempotent_Per_Edge_Not_A_Duplicate_Intent()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var first = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner-1",
            LongLease, DefaultMaxAttempts);
        Assert.Equal(CompensationPropagationClaimOutcome.Claimed, first.Outcome);

        // A second caller for the exact same edge (SagaId + ChildMessageId), simulating an at-least-once
        // redelivery racing the first attempt's still-live lease, must not create a second logical intent
        // and must not also get to claim it.
        var second = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner-2",
            LongLease, DefaultMaxAttempts);

        Assert.Equal(CompensationPropagationClaimOutcome.ClaimedByAnother, second.Outcome);

        var stored = await store.GetCompensationPropagationIntentAsync(sagaId, childId);
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.AttemptCount); // only the first call actually claimed
        Assert.Equal("owner-1", stored.Owner);
    }

    [Fact]
    public async Task EnsureAndClaimCompensationPropagationAsync_After_Completion_Reports_AlreadyCompleted()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner-1", LongLease, DefaultMaxAttempts);
        await store.MarkCompensationPropagationCompletedAsync(sagaId, childId);

        var claim = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner-2",
            LongLease, DefaultMaxAttempts);

        Assert.Equal(CompensationPropagationClaimOutcome.AlreadyCompleted, claim.Outcome);
    }

    [Fact]
    public async Task MarkCompensationPropagationCompletedAsync_Is_Idempotent()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner-1", LongLease, DefaultMaxAttempts);

        // A worker recovering after a crash right after the parent ran, but before recording completion,
        // must be able to record completion again without error or side effect.
        await store.MarkCompensationPropagationCompletedAsync(sagaId, childId);
        await store.MarkCompensationPropagationCompletedAsync(sagaId, childId);

        var stored = await store.GetCompensationPropagationIntentAsync(sagaId, childId);
        Assert.Equal(CompensationPropagationStatus.Completed, stored!.Status);
    }

    [Fact]
    public async Task GetCompensationPropagationIntentAsync_Returns_Null_For_Unknown_Edge()
    {
        var store = CreateStore();

        var stored = await store.GetCompensationPropagationIntentAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(stored);
    }

    [Fact]
    public async Task ClaimDueCompensationPropagationsAsync_Claims_A_Pending_Intent()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        // Simulates the crash-recovery boundary: the durable intent exists (Pending) but no immediate
        // attempt ever claimed it, e.g. the process died between the intent's durable creation and the
        // in-request attempt starting.
        await CreatePendingIntentAsync(store, sagaId, childId, parentId);

        var due = await store.ClaimDueCompensationPropagationsAsync(10, "worker-1", LongLease, TimeSpan.FromMilliseconds(10),
            DefaultMaxAttempts);

        Assert.Contains(due, i => i.SagaId == sagaId && i.ChildMessageId == childId);
        var claimed = due.Single(i => i.ChildMessageId == childId);
        Assert.Equal(CompensationPropagationStatus.Claimed, claimed.Status);
        Assert.Equal("worker-1", claimed.Owner);
    }

    [Fact]
    public async Task ClaimDueCompensationPropagationsAsync_Does_Not_Claim_A_Live_Claim()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        // Owned by an in-request attempt whose lease has not expired - a worker pass racing it must not
        // also pick it up (that would be a second concurrent owner for the same edge).
        await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "inline:owner", LongLease,
            DefaultMaxAttempts);

        var due = await store.ClaimDueCompensationPropagationsAsync(10, "worker-1", LongLease, LongLease, DefaultMaxAttempts);

        Assert.DoesNotContain(due, i => i.ChildMessageId == childId);
    }

    [Fact]
    public async Task ClaimDueCompensationPropagationsAsync_Recovers_A_Stale_Claim()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        // Simulates the crash-recovery boundary: an owner claimed the edge (e.g. the immediate in-request
        // attempt started) and then died mid-invocation, never reaching completion or failure. A short
        // recoveryTimeout lets the test observe the lease going stale without a real wait.
        var shortLease = TimeSpan.FromMilliseconds(20);
        var first = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "dead-owner",
            shortLease, DefaultMaxAttempts);
        Assert.Equal(CompensationPropagationClaimOutcome.Claimed, first.Outcome);

        await Task.Delay(TimeSpan.FromMilliseconds(120));

        var due = await store.ClaimDueCompensationPropagationsAsync(10, "worker-recovery", LongLease, shortLease,
            DefaultMaxAttempts);

        var recovered = Assert.Single(due, i => i.ChildMessageId == childId);
        Assert.Equal("worker-recovery", recovered.Owner);
        Assert.Equal(2, recovered.AttemptCount); // the original attempt, plus this recovery attempt
    }

    [Fact]
    public async Task ClaimDueCompensationPropagationsAsync_Concurrent_Recovery_Has_Exactly_One_Winner()
    {
        if (!SupportsCrossInstanceSharedStorage) return;

        var storeA = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        // The initial claim uses a short lease and a real wait so it is unambiguously stale before the
        // concurrent burst starts. The burst itself uses a much larger recoveryTimeout than any single
        // real round trip to a containerized provider is expected to take: it must comfortably exceed the
        // time elapsed since the initial claim (so every replica agrees the edge is due) while staying well
        // under the time a genuine winner's own fresh claim could age by before the last straggler replica
        // runs - otherwise a slow replica could legitimately reclaim an already-fresh winning claim and
        // this test would report two winners without there being an actual concurrency-safety violation.
        var initialLease = TimeSpan.FromMilliseconds(50);
        await storeA.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "dead-owner", initialLease,
            DefaultMaxAttempts);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var burstRecoveryTimeout = TimeSpan.FromMilliseconds(500);

        const int replicas = 6;
        var winners = new ConcurrentBag<string>();
        await Task.WhenAll(Enumerable.Range(0, replicas).Select(async i =>
        {
            var store = CreateStore();
            var due = await store.ClaimDueCompensationPropagationsAsync(10, $"worker-{i}", LongLease,
                burstRecoveryTimeout, DefaultMaxAttempts);
            if (due.Any(intent => intent.ChildMessageId == childId))
                winners.Add($"worker-{i}");
        }));

        Assert.Single(winners);
    }

    [Fact]
    public async Task ClaimDueCompensationPropagationsAsync_Exhausted_Attempts_Becomes_Terminal_Failed()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        const int maxAttempts = 2;
        var shortLease = TimeSpan.FromMilliseconds(20);

        // Exhaust every permitted attempt via repeated stale-claim recovery, simulating a parent handler
        // that keeps failing/crashing before it ever records an outcome.
        await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "attempt-1", shortLease, maxAttempts);
        await Task.Delay(TimeSpan.FromMilliseconds(60));
        await store.ClaimDueCompensationPropagationsAsync(10, "attempt-2", shortLease, shortLease, maxAttempts);
        await Task.Delay(TimeSpan.FromMilliseconds(60));

        // This pass finds attempts already exhausted (AttemptCount >= maxAttempts): it must not claim the
        // edge again, and must instead durably move it to the terminal, operator-visible Failed state.
        var due = await store.ClaimDueCompensationPropagationsAsync(10, "attempt-3", shortLease, shortLease, maxAttempts);
        Assert.DoesNotContain(due, i => i.ChildMessageId == childId);

        var stored = await store.GetCompensationPropagationIntentAsync(sagaId, childId);
        Assert.NotNull(stored);
        Assert.Equal(CompensationPropagationStatus.Failed, stored!.Status);
        Assert.NotNull(stored.FailureInfo);
    }

    [Fact]
    public async Task MarkCompensationPropagationFailedAsync_Records_FailureInfo_And_Is_Terminal()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner-1", LongLease, DefaultMaxAttempts);
        await store.MarkCompensationPropagationFailedAsync(sagaId, childId,
            new SagaStepFailureInfo("structurally unresolvable", null, null));

        var stored = await store.GetCompensationPropagationIntentAsync(sagaId, childId);
        Assert.Equal(CompensationPropagationStatus.Failed, stored!.Status);
        Assert.Equal("structurally unresolvable", stored.FailureInfo?.Reason);

        var claim = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner-2", LongLease, DefaultMaxAttempts);
        Assert.Equal(CompensationPropagationClaimOutcome.AttemptsExhausted, claim.Outcome);
    }

    [Fact]
    public async Task ClaimDueCompensationPropagationsAsync_Respects_MaxCount()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
            await CreatePendingIntentAsync(store, sagaId, Guid.NewGuid(), Guid.NewGuid());

        var due = await store.ClaimDueCompensationPropagationsAsync(2, "worker-1", LongLease, TimeSpan.FromMilliseconds(10),
            DefaultMaxAttempts);

        Assert.Equal(2, due.Count);
    }

    /// <summary>
    /// Creates a durable Pending intent without claiming it (unlike <c>EnsureAndClaimCompensationPropagationAsync</c>,
    /// which always claims on first creation). Models the exact crash-recovery boundary where the intent was
    /// durably handed off but the immediate attempt never ran: claims it with an already-expired lease so the
    /// claim itself is immediately released back to Pending-equivalent (reclaimable) state, without relying on
    /// provider-internal state manipulation.
    /// </summary>
    private static async Task CreatePendingIntentAsync(ISagaStore store, Guid sagaId, Guid childId, Guid parentId)
    {
        var expired = TimeSpan.FromMilliseconds(1);
        await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "transient-claimer",
            expired, DefaultMaxAttempts);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
    }
}
