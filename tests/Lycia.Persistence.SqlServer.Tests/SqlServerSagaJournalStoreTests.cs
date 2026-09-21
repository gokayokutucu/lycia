// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Saga.Abstractions.Persistence.Journal;

namespace Lycia.Persistence.SqlServer.Tests;

[Collection("SqlServerContainer")]
public class SqlServerSagaJournalStoreTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task AppendAsync_then_ReadAsync_roundtrips_all_fields()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var entry = NewEntry(sagaId, previousVersion: 0, targetVersion: 1);

        await store.AppendAsync(entry);
        var entries = await store.ReadAsync(sagaId, afterVersion: 0, maxCount: 10);

        var read = Assert.Single(entries);
        Assert.Equal(entry.JournalEntryId, read.JournalEntryId);
        Assert.Equal(entry.TransitionId, read.TransitionId);
        Assert.Equal(entry.SagaId, read.SagaId);
        Assert.Equal(entry.SequenceNumber, read.SequenceNumber);
        Assert.Equal(entry.PreviousVersion, read.PreviousVersion);
        Assert.Equal(entry.TargetVersion, read.TargetVersion);
        Assert.Equal(entry.MessageId, read.MessageId);
        Assert.Equal(entry.RequestId, read.RequestId);
        Assert.Equal(entry.CorrelationId, read.CorrelationId);
        Assert.Equal(entry.CausationId, read.CausationId);
        Assert.Equal(entry.ParentMessageId, read.ParentMessageId);
        Assert.Equal(entry.ApplicationId, read.ApplicationId);
        Assert.Equal(entry.HandlerType, read.HandlerType);
        Assert.Equal(entry.MessageType, read.MessageType);
        Assert.Equal(entry.MessageSchemaVersion, read.MessageSchemaVersion);
        Assert.Equal(entry.JournalSchemaVersion, read.JournalSchemaVersion);
        Assert.Equal(entry.TransitionType, read.TransitionType);
        Assert.Equal(entry.SagaDataTypeName, read.SagaDataTypeName);
        Assert.Equal(entry.SagaDataPayload, read.SagaDataPayload);
    }

    [Fact]
    public async Task ReadAsync_returns_ordered_sequence_after_multiple_appends()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();

        await store.AppendAsync(NewEntry(sagaId, 0, 1));
        await store.AppendAsync(NewEntry(sagaId, 1, 2));
        await store.AppendAsync(NewEntry(sagaId, 2, 3));

        var entries = await store.ReadAsync(sagaId, afterVersion: 0, maxCount: 10);

        Assert.Equal(3, entries.Count);
        Assert.Equal([1L, 2L, 3L], entries.Select(e => e.SequenceNumber).ToArray());
    }

    [Fact]
    public async Task AppendAsync_reappending_same_transition_is_a_no_op()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var entry = NewEntry(sagaId, 0, 1);

        await store.AppendAsync(entry);
        await store.AppendAsync(entry);

        var entries = await store.ReadAsync(sagaId, afterVersion: 0, maxCount: 10);
        Assert.Single(entries);
    }

    [Fact]
    public async Task GetLatestVersionAsync_returns_zero_for_unknown_saga_and_latest_value_after_appends()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();

        Assert.Equal(0, await store.GetLatestVersionAsync(sagaId));

        await store.AppendAsync(NewEntry(sagaId, 0, 1));
        await store.AppendAsync(NewEntry(sagaId, 1, 2));

        Assert.Equal(2, await store.GetLatestVersionAsync(sagaId));
    }

    [Fact]
    public async Task EnumerateSagaIdsAsync_pages_across_multiple_sagas_without_duplicates_or_omissions()
    {
        // The journal table is shared across every test in this collection, so a full cursor walk may
        // legitimately traverse SagaIds written by other tests too. What this test must prove is that
        // walking the whole table with the cursor sees every one of our own SagaIds exactly once, with
        // no duplicates anywhere in the walk (i.e. the cursor never re-visits or skips a page boundary).
        var store = CreateStore();
        var sagaIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var sagaId in sagaIds)
            await store.AppendAsync(NewEntry(sagaId, 0, 1));

        var seen = new List<Guid>();
        Guid? cursor = null;
        var guard = 0;
        while (true)
        {
            var page = await store.EnumerateSagaIdsAsync(cursor, 25);
            if (page.Count == 0) break;
            seen.AddRange(page);
            cursor = page[^1];
            if (++guard > 10_000) throw new InvalidOperationException("Cursor walk did not terminate.");
        }

        Assert.Equal(seen.Count, seen.Distinct().Count());
        foreach (var sagaId in sagaIds)
            Assert.Single(seen, x => x == sagaId);
    }

    [Fact]
    public async Task StepsSnapshotPayload_roundtrips_both_null_and_populated()
    {
        var store = CreateStore();
        var sagaWithNull = Guid.NewGuid();
        var sagaWithSteps = Guid.NewGuid();

        var withoutSteps = NewEntry(sagaWithNull, 0, 1);
        withoutSteps.StepsSnapshotPayload = null;
        var withSteps = NewEntry(sagaWithSteps, 0, 1);
        withSteps.StepsSnapshotPayload = "[{\"Step\":\"Started\"}]";

        await store.AppendAsync(withoutSteps);
        await store.AppendAsync(withSteps);

        var readNull = Assert.Single(await store.ReadAsync(sagaWithNull, 0, 10));
        var readWithSteps = Assert.Single(await store.ReadAsync(sagaWithSteps, 0, 10));

        Assert.Null(readNull.StepsSnapshotPayload);
        Assert.Equal(withSteps.StepsSnapshotPayload, readWithSteps.StepsSnapshotPayload);
    }

    [Fact]
    public async Task AppendAsync_duplicate_delivery_of_the_same_transition_never_produces_a_second_row()
    {
        // Simulates a redelivered message that the Inbox did not suppress: the same logical
        // transition (identical TransitionId derived from SagaId+TargetVersion) is appended twice.
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var transitionId = Guid.NewGuid();
        var first = NewEntry(sagaId, 0, 1);
        first.TransitionId = transitionId;
        var redelivered = NewEntry(sagaId, 0, 1);
        redelivered.TransitionId = transitionId;
        redelivered.JournalEntryId = Guid.NewGuid();

        await store.AppendAsync(first);
        await store.AppendAsync(redelivered);

        var entries = await store.ReadAsync(sagaId, 0, 10);
        Assert.Single(entries);
        Assert.Equal(1, await store.GetLatestVersionAsync(sagaId));
    }

    private SqlServerSagaJournalStore CreateStore()
    {
        var options = new SqlServerSagaStoreOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        SqlServerJournalSchemaMigrator.RunAsync(options).GetAwaiter().GetResult();
        return new SqlServerSagaJournalStore(options, null);
    }

    private static SagaJournalEntry NewEntry(Guid sagaId, long previousVersion, long targetVersion) => new()
    {
        JournalEntryId = Guid.NewGuid(),
        TransitionId = Guid.NewGuid(),
        SagaId = sagaId,
        SequenceNumber = targetVersion,
        PreviousVersion = previousVersion,
        TargetVersion = targetVersion,
        MessageId = Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        CausationId = Guid.NewGuid(),
        ParentMessageId = Guid.NewGuid(),
        ApplicationId = "journal-tests",
        HandlerType = "SomeHandler",
        MessageType = "SomeMessage",
        MessageSchemaVersion = 1,
        JournalSchemaVersion = SagaJournalSchema.CurrentVersion,
        TransitionType = targetVersion == 1 ? SagaJournalTransitionType.Created : SagaJournalTransitionType.Updated,
        SagaDataTypeName = "Lycia.Persistence.TestKit.DummySagaData",
        SagaDataPayload = $$"""{"SagaId":"{{sagaId}}","Version":{{targetVersion}}}""",
        StepsSnapshotPayload = null,
        CreatedAtUtc = DateTime.UtcNow
    };
}
