// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Outbox;

namespace Lycia.Persistence.TestKit;

/// <summary>Behavioral conformance suite shared by every <see cref="IOutboxStore"/> provider.</summary>
public abstract class OutboxStoreConformanceTests
{
    protected abstract IOutboxStore CreateStore();

    private static OutboxMessage NewMessage(Guid? messageId = null) =>
        new(messageId ?? Guid.NewGuid(), typeof(DummyEvent).FullName!, "{}", "TestApp", null);

    [Fact]
    public async Task AddAsync_Then_GetByMessageIdAsync_Roundtrips_As_Pending()
    {
        var store = CreateStore();
        var message = NewMessage();

        await store.AddAsync(message);
        var loaded = await store.GetByMessageIdAsync(message.MessageId);

        Assert.NotNull(loaded);
        Assert.Equal(OutboxMessageStatus.Pending, loaded!.Status);
    }

    [Fact]
    public async Task AddAsync_Duplicate_MessageId_Is_Idempotent_NoOp()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        await store.AddAsync(NewMessage(messageId));
        await store.MarkPublishedAsync(messageId);

        // Re-adding the same MessageId must not reset an already-advanced status.
        await store.AddAsync(NewMessage(messageId));
        var loaded = await store.GetByMessageIdAsync(messageId);

        Assert.Equal(OutboxMessageStatus.Published, loaded!.Status);
    }

    [Fact]
    public async Task GetByMessageIdAsync_For_Unknown_Message_Returns_Null()
    {
        var store = CreateStore();
        var loaded = await store.GetByMessageIdAsync(Guid.NewGuid());
        Assert.Null(loaded);
    }

    [Fact]
    public async Task ClaimPendingBatchAsync_Claims_Only_Pending_Messages()
    {
        var store = CreateStore();
        var pending = NewMessage();
        var alreadyPublished = NewMessage();
        await store.AddAsync(pending);
        await store.AddAsync(alreadyPublished);
        await store.MarkPublishedAsync(alreadyPublished.MessageId);

        var claimed = await store.ClaimPendingBatchAsync(10);

        Assert.Contains(claimed, m => m.MessageId == pending.MessageId);
        Assert.DoesNotContain(claimed, m => m.MessageId == alreadyPublished.MessageId);
        Assert.Equal(OutboxMessageStatus.Claimed, (await store.GetByMessageIdAsync(pending.MessageId))!.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Status_Transitions_Report_Correctly(bool confirmed)
    {
        var store = CreateStore();
        var message = NewMessage();
        await store.AddAsync(message);
        await store.ClaimPendingBatchAsync(10);

        await store.MarkPublishingAsync(message.MessageId);
        Assert.Equal(OutboxMessageStatus.Publishing, (await store.GetByMessageIdAsync(message.MessageId))!.Status);

        if (confirmed)
        {
            await store.MarkPublishedAsync(message.MessageId);
            Assert.Equal(OutboxMessageStatus.Published, (await store.GetByMessageIdAsync(message.MessageId))!.Status);
        }
        else
        {
            await store.MarkConfirmationUnknownAsync(message.MessageId);
            Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, (await store.GetByMessageIdAsync(message.MessageId))!.Status);
        }
    }

    [Fact]
    public async Task MarkFailedAsync_Sets_Failed_Status_And_FailureInfo()
    {
        var store = CreateStore();
        var message = NewMessage();
        await store.AddAsync(message);

        var failureInfo = new Lycia.Common.SagaSteps.SagaStepFailureInfo("broker unreachable", nameof(TimeoutException), null);
        await store.MarkFailedAsync(message.MessageId, failureInfo);

        var loaded = await store.GetByMessageIdAsync(message.MessageId);
        Assert.Equal(OutboxMessageStatus.Failed, loaded!.Status);
        Assert.Equal("broker unreachable", loaded.FailureInfo?.Reason);
    }

    // The relational providers keep every conformance test's rows in one shared table, so these tests
    // claim a large batch and look for their own message instead of asserting on "the" claimed row or on
    // an empty result. Each test also passes the same recovery window to every claim, exactly as the
    // worker does: Redis fixes a claimed message's next eligibility at claim time, so reclaiming with a
    // different window than the claim used would not model real operation.
    private const int WholeTable = 10_000;

    private static bool ContainsMessage(IReadOnlyList<OutboxMessage> claimed, Guid messageId) =>
        claimed.Any(candidate => candidate.MessageId == messageId);

    [Fact]
    public async Task ConfirmationUnknown_Is_Reclaimable_Until_MaxAttempts()
    {
        var store = CreateStore();
        var message = NewMessage();
        var recoveryTimeout = TimeSpan.FromMilliseconds(200);
        await store.AddAsync(message);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.True(ContainsMessage(
                await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: 3, recoveryTimeout: recoveryTimeout),
                message.MessageId), $"attempt {attempt} did not claim the message");
            await store.MarkPublishingAsync(message.MessageId);
            await store.MarkConfirmationUnknownAsync(message.MessageId);
            Assert.Equal(attempt, (await store.GetByMessageIdAsync(message.MessageId))!.RetryCount);
            await Task.Delay(600);
        }

        Assert.False(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: 3, recoveryTimeout: recoveryTimeout),
            message.MessageId));
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown,
            (await store.GetByMessageIdAsync(message.MessageId))!.Status);
    }

    /// <summary>
    /// An unconfirmed message must wait out the recovery window before it is handed back. Without this,
    /// every attempt permitted by <c>maxAttempts</c> is consumed within seconds of the first one and the
    /// same message is republished that many times in a burst.
    /// </summary>
    [Fact]
    public async Task ConfirmationUnknown_Is_Not_Reclaimed_Before_The_Recovery_Window_Elapses()
    {
        var store = CreateStore();
        var message = NewMessage();
        var recoveryTimeout = TimeSpan.FromSeconds(2);
        await store.AddAsync(message);

        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, recoveryTimeout: recoveryTimeout), message.MessageId));
        await store.MarkPublishingAsync(message.MessageId);
        await store.MarkConfirmationUnknownAsync(message.MessageId);

        Assert.False(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, recoveryTimeout: recoveryTimeout), message.MessageId));

        // ...and it is handed back once that window has passed.
        await Task.Delay(recoveryTimeout + TimeSpan.FromSeconds(1));
        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, recoveryTimeout: recoveryTimeout), message.MessageId));
    }

    /// <summary>
    /// The terminal state for exhausted attempts. It must be distinct from both Published and Failed, must
    /// carry the recorded reason, and must never be handed back by a claim query again.
    /// </summary>
    [Fact]
    public async Task MarkAbandonedAsync_Is_Terminal_And_Records_Why()
    {
        var store = CreateStore();
        var message = NewMessage();
        await store.AddAsync(message);
        Assert.True(ContainsMessage(await store.ClaimPendingBatchAsync(WholeTable), message.MessageId));

        var failureInfo = new Lycia.Common.SagaSteps.SagaStepFailureInfo(
            "attempts exhausted without confirmation", nameof(TimeoutException), null);
        await store.MarkAbandonedAsync(message.MessageId, failureInfo);

        var loaded = await store.GetByMessageIdAsync(message.MessageId);
        Assert.Equal(OutboxMessageStatus.Abandoned, loaded!.Status);
        Assert.Equal("attempts exhausted without confirmation", loaded.FailureInfo?.Reason);

        // Terminal: not claimable again, even with the recovery window fully elapsed.
        Assert.False(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, recoveryTimeout: TimeSpan.Zero), message.MessageId));
    }

    [Fact]
    public async Task Expired_Claimed_And_Publishing_Records_Are_Recovered_After_Worker_Restart()
    {
        var store = CreateStore();
        var message = NewMessage();
        var recoveryTimeout = TimeSpan.FromMilliseconds(10);
        await store.AddAsync(message);
        Assert.Contains(await store.ClaimPendingBatchAsync(10_000, recoveryTimeout: recoveryTimeout),
            candidate => candidate.MessageId == message.MessageId);

        await Task.Delay(50);
        Assert.Equal(message.MessageId,
            Assert.Single(await store.ClaimPendingBatchAsync(10_000, recoveryTimeout: recoveryTimeout),
                candidate => candidate.MessageId == message.MessageId).MessageId);

        await store.MarkPublishingAsync(message.MessageId);
        await Task.Delay(50);
        Assert.Equal(message.MessageId,
            Assert.Single(await store.ClaimPendingBatchAsync(10_000, recoveryTimeout: recoveryTimeout),
                candidate => candidate.MessageId == message.MessageId).MessageId);
    }

    // --- Crash recovery of an in-doubt attempt --------------------------------------------------------
    //
    // MarkPublishingAsync counts an attempt when it STARTS, so a worker that dies after that call leaves a
    // Publishing row whose attempt has no recorded outcome. Whether that attempt reached the broker is
    // unknowable, so the row must stay visible to recovery even when its RetryCount has already reached
    // the cap: the cap limits how many attempts may be started, not whether an in-doubt one gets resolved.

    private static OutboxMessage? Find(IReadOnlyList<OutboxMessage> claimed, Guid messageId) =>
        claimed.FirstOrDefault(candidate => candidate.MessageId == messageId);

    [Fact]
    public async Task Stale_Publishing_Row_At_The_Attempt_Cap_Is_Handed_Back_For_Recovery()
    {
        const int maxAttempts = 2;
        var store = CreateStore();
        var message = NewMessage();
        var recoveryTimeout = TimeSpan.FromMilliseconds(200);
        await store.AddAsync(message);

        // Attempt 1 completes without a confirmation.
        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId));
        await store.MarkPublishingAsync(message.MessageId);
        await store.MarkConfirmationUnknownAsync(message.MessageId);
        await Task.Delay(600);

        // Attempt 2, the last permitted one, starts - and the worker dies before recording any outcome.
        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId));
        await store.MarkPublishingAsync(message.MessageId);
        var inDoubt = (await store.GetByMessageIdAsync(message.MessageId))!;
        Assert.Equal(OutboxMessageStatus.Publishing, inDoubt.Status);
        Assert.Equal(maxAttempts, inDoubt.RetryCount);

        await Task.Delay(600);

        var recovered = Find(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId);
        Assert.True(recovered != null,
            "A Publishing row whose worker died on the final attempt was never handed back: the message is " +
            "stranded with no retry and no terminal state.");
        // The attempts already started are preserved for the dispatcher to reason about, never reset.
        Assert.Equal(maxAttempts, recovered!.RetryCount);
        Assert.Equal(message.MessageId, recovered.MessageId);
    }

    [Fact]
    public async Task Stale_Claimed_Row_At_The_Attempt_Cap_Is_Still_Handed_Back()
    {
        // A recovery claim that itself dies before MarkPublishingAsync leaves a Claimed row at the cap.
        const int maxAttempts = 1;
        var store = CreateStore();
        var message = NewMessage();
        var recoveryTimeout = TimeSpan.FromMilliseconds(200);
        await store.AddAsync(message);
        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId));
        await store.MarkPublishingAsync(message.MessageId);
        await Task.Delay(600);

        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId), "the in-doubt Publishing row was not recovered");
        Assert.Equal(OutboxMessageStatus.Claimed, (await store.GetByMessageIdAsync(message.MessageId))!.Status);
        await Task.Delay(600);

        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId), "a stale Claimed row at the attempt cap was not handed back");
    }

    [Fact]
    public async Task A_Recovering_Row_Is_Not_Handed_Back_Before_The_Recovery_Window_Elapses()
    {
        const int maxAttempts = 1;
        var store = CreateStore();
        var message = NewMessage();
        var recoveryTimeout = TimeSpan.FromSeconds(2);
        await store.AddAsync(message);
        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId));
        await store.MarkPublishingAsync(message.MessageId);

        // A live worker is still inside its window: nothing may take the row over yet.
        Assert.False(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId));

        await Task.Delay(recoveryTimeout + TimeSpan.FromSeconds(1));
        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId));
    }

    /// <summary>
    /// Two replicas racing for the same stale in-doubt row must not both take ownership of it. The window
    /// is longer than the spread of the racers' arrival times, for the reason given in the Inbox suite.
    /// </summary>
    [Fact]
    public async Task Concurrent_Recovery_Of_A_Stale_Final_Attempt_Row_Has_Exactly_One_Owner()
    {
        const int maxAttempts = 1;
        var store = CreateStore();
        var message = NewMessage();
        var window = TimeSpan.FromSeconds(2);
        await store.AddAsync(message);
        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: window),
            message.MessageId));
        await store.MarkPublishingAsync(message.MessageId);
        await Task.Delay(window + TimeSpan.FromMilliseconds(500));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: window))));

        Assert.Equal(1, results.Count(claimed => ContainsMessage(claimed, message.MessageId)));
    }

    [Theory]
    [InlineData(OutboxMessageStatus.Published)]
    [InlineData(OutboxMessageStatus.Failed)]
    [InlineData(OutboxMessageStatus.Abandoned)]
    public async Task Terminal_Rows_Are_Never_Handed_Back_Even_When_Stale_And_At_The_Cap(OutboxMessageStatus terminal)
    {
        const int maxAttempts = 1;
        var store = CreateStore();
        var message = NewMessage();
        var recoveryTimeout = TimeSpan.FromMilliseconds(100);
        await store.AddAsync(message);
        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId));
        await store.MarkPublishingAsync(message.MessageId);
        var info = new Lycia.Common.SagaSteps.SagaStepFailureInfo("terminal", "Test", null);
        switch (terminal)
        {
            case OutboxMessageStatus.Published: await store.MarkPublishedAsync(message.MessageId); break;
            case OutboxMessageStatus.Failed: await store.MarkFailedAsync(message.MessageId, info); break;
            default: await store.MarkAbandonedAsync(message.MessageId, info); break;
        }

        await Task.Delay(600);

        Assert.False(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId));
        Assert.Equal(terminal, (await store.GetByMessageIdAsync(message.MessageId))!.Status);
    }

    /// <summary>
    /// The dispatcher bounds crash recovery by comparing the returned attempt count with the cap, so the
    /// store must keep counting past the cap and hand the row back with that count intact, however many
    /// workers have died on it. Clamping the count, or dropping the row at the cap, would either loop
    /// forever or strand the message.
    /// </summary>
    [Fact]
    public async Task A_Recovery_Attempt_That_Also_Dies_Is_Handed_Back_With_Its_Full_Attempt_Count()
    {
        const int maxAttempts = 1;
        var store = CreateStore();
        var message = NewMessage();
        var recoveryTimeout = TimeSpan.FromMilliseconds(200);
        await store.AddAsync(message);

        // The final attempt starts and its worker dies.
        Assert.True(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId));
        await store.MarkPublishingAsync(message.MessageId);
        await Task.Delay(600);

        // The recovery attempt starts (one past the cap) and its worker dies as well.
        var recovered = Find(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId);
        Assert.True(recovered != null, "the in-doubt final attempt was not recovered");
        Assert.Equal(maxAttempts, recovered!.RetryCount);
        await store.MarkPublishingAsync(message.MessageId);
        Assert.Equal(maxAttempts + 1, (await store.GetByMessageIdAsync(message.MessageId))!.RetryCount);
        await Task.Delay(600);

        var handedBackAgain = Find(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId);
        Assert.True(handedBackAgain != null,
            "a recovery attempt whose worker died was not handed back, so nothing can make it operator-visible");
        Assert.Equal(maxAttempts + 1, handedBackAgain!.RetryCount);

        // Once the dispatcher has made it terminal it is gone from the claim queue for good.
        await store.MarkAbandonedAsync(message.MessageId,
            new Lycia.Common.SagaSteps.SagaStepFailureInfo("workers stopped repeatedly", "Abandoned", null));
        await Task.Delay(600);
        Assert.False(ContainsMessage(
            await store.ClaimPendingBatchAsync(WholeTable, maxAttempts: maxAttempts, recoveryTimeout: recoveryTimeout),
            message.MessageId));
    }
}
