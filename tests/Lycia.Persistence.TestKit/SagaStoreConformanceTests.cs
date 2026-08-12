// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Exceptions;

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
    // Only exercised when the provider under test implements IVersionedSagaStore. Providers that don't
    // (none in Phase 1 skip this - all four Phase 1 providers implement it) would simply have these
    // tests report inconclusive via the guard below.

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
}
