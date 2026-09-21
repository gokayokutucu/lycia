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
}
