// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Inbox;

namespace Lycia.Persistence.TestKit;

/// <summary>Behavioral conformance suite shared by every <see cref="IInboxStore"/> provider.</summary>
public abstract class InboxStoreConformanceTests
{
    /// <summary>
    /// Creates a store whose claim-recovery window is <paramref name="claimRecoveryTimeout"/>, i.e. how
    /// long a <c>Processing</c> claim or a <c>Failed</c> record stays protected from being claimed again.
    /// </summary>
    protected abstract IInboxStore CreateStore(TimeSpan claimRecoveryTimeout);

    /// <summary>A store with a recovery window long enough that no takeover happens mid-test.</summary>
    private IInboxStore CreateStore() => CreateStore(TimeSpan.FromMinutes(5));

    private static readonly Type HandlerType = typeof(DummySagaHandler);

    [Fact]
    public async Task TryBeginAsync_First_Delivery_Returns_Started()
    {
        var store = CreateStore();
        var result = await store.TryBeginAsync(Guid.NewGuid(), HandlerType);
        Assert.Equal(InboxBeginResult.Started, result);
    }

    [Fact]
    public async Task TryBeginAsync_Duplicate_While_Processing_Returns_AlreadyProcessing()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        await store.TryBeginAsync(messageId, HandlerType);

        var result = await store.TryBeginAsync(messageId, HandlerType);

        Assert.Equal(InboxBeginResult.AlreadyProcessing, result);
    }

    [Fact]
    public async Task TryBeginAsync_After_Completed_Returns_AlreadyCompleted_And_Stays_Completed()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        await store.TryBeginAsync(messageId, HandlerType);
        await store.MarkCompletedAsync(messageId, HandlerType);

        var result = await store.TryBeginAsync(messageId, HandlerType);
        var status = await store.GetStatusAsync(messageId, HandlerType);

        Assert.Equal(InboxBeginResult.AlreadyCompleted, result);
        Assert.Equal(InboxMessageStatus.Completed, status);
    }

    [Fact]
    public async Task TryBeginAsync_After_Failed_Returns_AlreadyFailed()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        await store.TryBeginAsync(messageId, HandlerType);
        await store.MarkFailedAsync(messageId, HandlerType, null);

        var result = await store.TryBeginAsync(messageId, HandlerType);
        var status = await store.GetStatusAsync(messageId, HandlerType);

        Assert.Equal(InboxBeginResult.AlreadyFailed, result);
        Assert.Equal(InboxMessageStatus.Failed, status);
    }

    [Fact]
    public async Task GetStatusAsync_For_Unknown_Message_Returns_None()
    {
        var store = CreateStore();
        var status = await store.GetStatusAsync(Guid.NewGuid(), HandlerType);
        Assert.Equal(InboxMessageStatus.None, status);
    }

    /// <summary>
    /// A process that dies after committing its claim but before completing the handler leaves the record
    /// in <c>Processing</c>. Once the recovery window elapses a later delivery must be able to take the
    /// claim over — otherwise every redelivery is skipped forever and the work is silently lost.
    /// </summary>
    [Fact]
    public async Task TryBeginAsync_Takes_Over_A_Processing_Claim_Abandoned_By_A_Dead_Process()
    {
        var store = CreateStore(TimeSpan.FromMilliseconds(10));
        var messageId = Guid.NewGuid();
        Assert.Equal(InboxBeginResult.Started, await store.TryBeginAsync(messageId, HandlerType));

        await Task.Delay(60);

        Assert.Equal(InboxBeginResult.Started, await store.TryBeginAsync(messageId, HandlerType));
        Assert.Equal(InboxMessageStatus.Processing, await store.GetStatusAsync(messageId, HandlerType));
    }

    /// <summary>
    /// A failed attempt must become retryable once its suppression window passes, so a message replayed
    /// from a dead-letter queue is actually reprocessed instead of suppressed permanently.
    /// </summary>
    [Fact]
    public async Task TryBeginAsync_Allows_Retrying_A_Failed_Record_After_The_Recovery_Window()
    {
        var store = CreateStore(TimeSpan.FromMilliseconds(10));
        var messageId = Guid.NewGuid();
        await store.TryBeginAsync(messageId, HandlerType);
        await store.MarkFailedAsync(messageId, HandlerType, null);

        await Task.Delay(60);

        Assert.Equal(InboxBeginResult.Started, await store.TryBeginAsync(messageId, HandlerType));
        Assert.Equal(InboxMessageStatus.Processing, await store.GetStatusAsync(messageId, HandlerType));
    }

    /// <summary>
    /// Completed work is never reclaimable, however long ago it completed. This is the duplicate
    /// suppression the Inbox exists for, and the recovery window must not weaken it.
    /// </summary>
    [Fact]
    public async Task TryBeginAsync_Never_Reclaims_A_Completed_Record_Even_When_Stale()
    {
        var store = CreateStore(TimeSpan.FromMilliseconds(10));
        var messageId = Guid.NewGuid();
        await store.TryBeginAsync(messageId, HandlerType);
        await store.MarkCompletedAsync(messageId, HandlerType);

        await Task.Delay(60);

        Assert.Equal(InboxBeginResult.AlreadyCompleted, await store.TryBeginAsync(messageId, HandlerType));
        Assert.Equal(InboxMessageStatus.Completed, await store.GetStatusAsync(messageId, HandlerType));
    }

    /// <summary>Exactly one of several concurrent deliveries may take over a stale claim.</summary>
    /// <remarks>
    /// The recovery window has to be longer than the spread of the racers' arrival times. Each racer
    /// opens its own connection, so they do not arrive simultaneously; with a very short window a racer
    /// arriving after the winner's takeover would legitimately find even the refreshed claim stale and
    /// take it over again. That is correct behavior for such a window, but it would hide whether two
    /// genuinely concurrent takeovers can both succeed, which is what this test exists to check.
    /// </remarks>
    [Fact]
    public async Task Concurrent_TryBeginAsync_On_A_Stale_Claim_Has_Exactly_One_Winner()
    {
        var window = TimeSpan.FromSeconds(2);
        var store = CreateStore(window);
        var messageId = Guid.NewGuid();
        await store.TryBeginAsync(messageId, HandlerType);

        await Task.Delay(window + TimeSpan.FromMilliseconds(500));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => store.TryBeginAsync(messageId, HandlerType))));

        Assert.Equal(1, attempts.Count(result => result == InboxBeginResult.Started));
    }

    [Fact]
    public async Task Same_MessageId_Different_HandlerType_Are_Independent()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        await store.TryBeginAsync(messageId, typeof(DummySagaHandler));
        await store.MarkCompletedAsync(messageId, typeof(DummySagaHandler));

        var resultForOtherHandler = await store.TryBeginAsync(messageId, typeof(DummyEvent));

        Assert.Equal(InboxBeginResult.Started, resultForOtherHandler);
    }
}
