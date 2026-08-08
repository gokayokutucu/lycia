// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Inbox;

namespace Lycia.Persistence.TestKit;

/// <summary>Behavioral conformance suite shared by every <see cref="IInboxStore"/> provider.</summary>
public abstract class InboxStoreConformanceTests
{
    protected abstract IInboxStore CreateStore();

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
