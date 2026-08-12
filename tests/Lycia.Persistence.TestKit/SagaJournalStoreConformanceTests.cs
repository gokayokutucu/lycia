// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Saga.Abstractions.Persistence.Journal;

namespace Lycia.Persistence.TestKit;

/// <summary>Behavioral conformance suite shared by every <see cref="ISagaJournalStore"/> provider.</summary>
public abstract class SagaJournalStoreConformanceTests
{
    protected abstract ISagaJournalStore CreateStore();

    private static SagaJournalEntry NewEntry(Guid sagaId, long previousVersion, long targetVersion, Guid? messageId = null) =>
        new()
        {
            JournalEntryId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(),
            SagaId = sagaId,
            SequenceNumber = targetVersion,
            PreviousVersion = previousVersion,
            TargetVersion = targetVersion,
            MessageId = messageId ?? Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            ParentMessageId = Guid.NewGuid(),
            ApplicationId = "TestApp",
            HandlerType = "TestHandler",
            MessageType = "TestMessage",
            TransitionType = targetVersion == 1 ? SagaJournalTransitionType.Created : SagaJournalTransitionType.Updated,
            SagaDataTypeName = "TestSagaData",
            SagaDataPayload = "{\"value\":1}",
            StepsSnapshotPayload = "[{\"step\":1}]",
            CreatedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task AppendAsync_First_Transition_Roundtrips_All_Fields()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var entry = NewEntry(sagaId, 0, 1);

        await store.AppendAsync(entry);
        var read = await store.ReadAsync(sagaId, 0, 10);

        var roundtripped = Assert.Single(read);
        Assert.Equal(entry.TransitionId, roundtripped.TransitionId);
        Assert.Equal(entry.SagaId, roundtripped.SagaId);
        Assert.Equal(entry.SequenceNumber, roundtripped.SequenceNumber);
        Assert.Equal(entry.PreviousVersion, roundtripped.PreviousVersion);
        Assert.Equal(entry.TargetVersion, roundtripped.TargetVersion);
        Assert.Equal(entry.MessageId, roundtripped.MessageId);
        Assert.Equal(entry.RequestId, roundtripped.RequestId);
        Assert.Equal(entry.CorrelationId, roundtripped.CorrelationId);
        Assert.Equal(entry.CausationId, roundtripped.CausationId);
        Assert.Equal(entry.ParentMessageId, roundtripped.ParentMessageId);
        Assert.Equal(entry.ApplicationId, roundtripped.ApplicationId);
        Assert.Equal(entry.HandlerType, roundtripped.HandlerType);
        Assert.Equal(entry.MessageType, roundtripped.MessageType);
        Assert.Equal(entry.TransitionType, roundtripped.TransitionType);
        Assert.Equal(entry.SagaDataTypeName, roundtripped.SagaDataTypeName);
        Assert.Equal(entry.SagaDataPayload, roundtripped.SagaDataPayload);
        Assert.Equal(entry.StepsSnapshotPayload, roundtripped.StepsSnapshotPayload);
    }

    [Fact]
    public async Task ReadAsync_Returns_Ordered_Sequence()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        await store.AppendAsync(NewEntry(sagaId, 0, 1));
        await store.AppendAsync(NewEntry(sagaId, 1, 2));
        await store.AppendAsync(NewEntry(sagaId, 2, 3));

        var read = await store.ReadAsync(sagaId, 0, 10);

        Assert.Equal([1L, 2L, 3L], read.Select(e => e.SequenceNumber));
    }

    [Fact]
    public async Task AppendAsync_Duplicate_TransitionId_Is_Idempotent_NoOp()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var entry = NewEntry(sagaId, 0, 1);

        await store.AppendAsync(entry);
        await store.AppendAsync(entry);

        var read = await store.ReadAsync(sagaId, 0, 10);
        Assert.Single(read);
    }

    [Fact]
    public async Task GetLatestVersionAsync_Returns_Zero_For_Unknown_Saga()
    {
        var store = CreateStore();
        Assert.Equal(0, await store.GetLatestVersionAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetLatestVersionAsync_Returns_Highest_Committed_Version()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        await store.AppendAsync(NewEntry(sagaId, 0, 1));
        await store.AppendAsync(NewEntry(sagaId, 1, 2));

        Assert.Equal(2, await store.GetLatestVersionAsync(sagaId));
    }

    [Fact]
    public async Task ReadAsync_AfterVersion_Only_Returns_Later_Entries()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        await store.AppendAsync(NewEntry(sagaId, 0, 1));
        await store.AppendAsync(NewEntry(sagaId, 1, 2));
        await store.AppendAsync(NewEntry(sagaId, 2, 3));

        var read = await store.ReadAsync(sagaId, 1, 10);

        Assert.Equal([2L, 3L], read.Select(e => e.SequenceNumber));
    }

    [Fact]
    public async Task EnumerateSagaIdsAsync_Covers_All_Sagas_Across_Pages()
    {
        var store = CreateStore();
        var sagaIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        foreach (var sagaId in sagaIds)
            await store.AppendAsync(NewEntry(sagaId, 0, 1));

        var discovered = new List<Guid>();
        Guid? cursor = null;
        while (true)
        {
            var page = await store.EnumerateSagaIdsAsync(cursor, 2);
            if (page.Count == 0) break;
            discovered.AddRange(page);
            cursor = page[^1];
        }

        Assert.Equal(sagaIds.Count, discovered.Distinct().Count());
        foreach (var sagaId in sagaIds)
            Assert.Contains(sagaId, discovered);
    }
}
