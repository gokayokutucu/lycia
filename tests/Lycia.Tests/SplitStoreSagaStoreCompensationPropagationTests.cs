// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.SagaSteps;
using Lycia.Extensions.SplitStore;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Moq;

namespace Lycia.Tests;

/// <summary>
/// Split Store's compensation-propagation methods must be pure pass-throughs to the canonical relational
/// store: the canonical store, not the Redis operational projection, is the sole authority for compensation
/// propagation durability. These tests prove wiring, not behavior - the behavior itself is proven by the
/// canonical provider's own <c>SagaStoreConformanceTests</c> coverage.
/// </summary>
public class SplitStoreSagaStoreCompensationPropagationTests
{
    private static (SplitStoreSagaStore Store, Mock<ISagaStore> Canonical) CreateStore()
    {
        var canonical = new Mock<ISagaStore>();
        var reconciliation = Mock.Of<IReconciliationStore>();
        var journal = Mock.Of<ISagaJournalStore>();
        var store = new SplitStoreSagaStore(canonical.Object, reconciliation, journal, null);
        return (store, canonical);
    }

    [Fact]
    public async Task EnsureAndClaimCompensationPropagationAsync_Delegates_To_Canonical_Store()
    {
        var (store, canonical) = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var lease = TimeSpan.FromMinutes(1);
        var expected = new CompensationPropagationClaim(CompensationPropagationClaimOutcome.Claimed,
            new CompensationPropagationIntent { SagaId = sagaId, ChildMessageId = childId });
        canonical.Setup(c => c.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner", lease,
                5, CancellationToken.None))
            .ReturnsAsync(expected);

        var result = await store.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner", lease, 5);

        Assert.Equal(expected.Outcome, result.Outcome);
        canonical.Verify(c => c.EnsureAndClaimCompensationPropagationAsync(sagaId, childId, parentId, "owner", lease,
            5, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ClaimDueCompensationPropagationsAsync_Delegates_To_Canonical_Store()
    {
        var (store, canonical) = CreateStore();
        var lease = TimeSpan.FromMinutes(1);
        var recovery = TimeSpan.FromMinutes(2);
        IReadOnlyList<CompensationPropagationIntent> expected = [new CompensationPropagationIntent()];
        canonical.Setup(c => c.ClaimDueCompensationPropagationsAsync(50, "worker", lease, recovery, 5, CancellationToken.None))
            .ReturnsAsync(expected);

        var result = await store.ClaimDueCompensationPropagationsAsync(50, "worker", lease, recovery, 5);

        Assert.Same(expected, result);
        canonical.Verify(c => c.ClaimDueCompensationPropagationsAsync(50, "worker", lease, recovery, 5, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task MarkCompensationPropagationCompletedAsync_Delegates_To_Canonical_Store()
    {
        var (store, canonical) = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        await store.MarkCompensationPropagationCompletedAsync(sagaId, childId);

        canonical.Verify(c => c.MarkCompensationPropagationCompletedAsync(sagaId, childId, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task MarkCompensationPropagationFailedAsync_Delegates_To_Canonical_Store()
    {
        var (store, canonical) = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var failureInfo = new SagaStepFailureInfo("reason", null, null);

        await store.MarkCompensationPropagationFailedAsync(sagaId, childId, failureInfo);

        canonical.Verify(c => c.MarkCompensationPropagationFailedAsync(sagaId, childId, failureInfo, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task GetCompensationPropagationIntentAsync_Delegates_To_Canonical_Store()
    {
        var (store, canonical) = CreateStore();
        var sagaId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var expected = new CompensationPropagationIntent { SagaId = sagaId, ChildMessageId = childId };
        canonical.Setup(c => c.GetCompensationPropagationIntentAsync(sagaId, childId, CancellationToken.None))
            .ReturnsAsync(expected);

        var result = await store.GetCompensationPropagationIntentAsync(sagaId, childId);

        Assert.Same(expected, result);
    }
}
