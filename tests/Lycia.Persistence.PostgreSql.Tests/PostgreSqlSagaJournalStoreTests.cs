// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Saga.Abstractions.Persistence.Journal;

namespace Lycia.Persistence.PostgreSql.Tests;

[Collection("PostgreSqlContainer")]
public class PostgreSqlSagaJournalStoreTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task Append_first_transition_round_trips_all_fields()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();

        var entry = new SagaJournalEntry
        {
            JournalEntryId = Guid.NewGuid(),
            TransitionId = Guid.NewGuid(),
            SagaId = sagaId,
            SequenceNumber = 1,
            PreviousVersion = 0,
            TargetVersion = 1,
            MessageId = messageId,
            RequestId = requestId,
            CorrelationId = correlationId,
            CausationId = causationId,
            ParentMessageId = parentMessageId,
            ApplicationId = "app-1",
            HandlerType = "MyHandler",
            MessageType = "MyMessage",
            MessageSchemaVersion = 2,
            JournalSchemaVersion = 1,
            TransitionType = SagaJournalTransitionType.Created,
            SagaDataTypeName = "MySagaData",
            SagaDataPayload = "{\"Value\":42}",
            StepsSnapshotPayload = "{\"Steps\":[]}",
            CreatedAtUtc = DateTime.UtcNow
        };

        await store.AppendAsync(entry);

        var read = await store.ReadAsync(sagaId, 0, 10);
        var actual = Assert.Single(read);

        Assert.Equal(entry.JournalEntryId, actual.JournalEntryId);
        Assert.Equal(entry.TransitionId, actual.TransitionId);
        Assert.Equal(entry.SagaId, actual.SagaId);
        Assert.Equal(entry.SequenceNumber, actual.SequenceNumber);
        Assert.Equal(entry.PreviousVersion, actual.PreviousVersion);
        Assert.Equal(entry.TargetVersion, actual.TargetVersion);
        Assert.Equal(entry.MessageId, actual.MessageId);
        Assert.Equal(entry.RequestId, actual.RequestId);
        Assert.Equal(entry.CorrelationId, actual.CorrelationId);
        Assert.Equal(entry.CausationId, actual.CausationId);
        Assert.Equal(entry.ParentMessageId, actual.ParentMessageId);
        Assert.Equal(entry.ApplicationId, actual.ApplicationId);
        Assert.Equal(entry.HandlerType, actual.HandlerType);
        Assert.Equal(entry.MessageType, actual.MessageType);
        Assert.Equal(entry.MessageSchemaVersion, actual.MessageSchemaVersion);
        Assert.Equal(entry.JournalSchemaVersion, actual.JournalSchemaVersion);
        Assert.Equal(entry.TransitionType, actual.TransitionType);
        Assert.Equal(entry.SagaDataTypeName, actual.SagaDataTypeName);
        Assert.Equal("42", System.Text.Json.JsonDocument.Parse(actual.SagaDataPayload).RootElement.GetProperty("Value").ToString());
        Assert.NotNull(actual.StepsSnapshotPayload);
    }

    [Fact]
    public async Task Ordered_sequence_reads_back_in_order()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();

        await store.AppendAsync(BuildEntry(sagaId, 0, 1));
        await store.AppendAsync(BuildEntry(sagaId, 1, 2));
        await store.AppendAsync(BuildEntry(sagaId, 2, 3));

        var read = await store.ReadAsync(sagaId, 0, 10);
        Assert.Equal(3, read.Count);
        Assert.Equal([1L, 2L, 3L], read.Select(x => x.TargetVersion).ToArray());
        Assert.Equal([1L, 2L, 3L], read.Select(x => x.SequenceNumber).ToArray());
    }

    [Fact]
    public async Task Reappending_same_transition_id_is_a_no_op()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();
        var entry = BuildEntry(sagaId, 0, 1);

        await store.AppendAsync(entry);
        await store.AppendAsync(entry);

        var read = await store.ReadAsync(sagaId, 0, 10);
        Assert.Single(read);
    }

    [Fact]
    public async Task GetLatestVersion_returns_zero_for_unknown_saga_and_correct_value_after_appends()
    {
        var store = CreateStore();
        var sagaId = Guid.NewGuid();

        Assert.Equal(0, await store.GetLatestVersionAsync(sagaId));

        await store.AppendAsync(BuildEntry(sagaId, 0, 1));
        await store.AppendAsync(BuildEntry(sagaId, 1, 2));

        Assert.Equal(2, await store.GetLatestVersionAsync(sagaId));
    }

    [Fact]
    public async Task EnumerateSagaIds_pages_through_every_saga_with_no_duplicates_or_omissions()
    {
        var store = CreateStore();
        var sagaIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).OrderBy(x => x).ToArray();

        foreach (var sagaId in sagaIds)
            await store.AppendAsync(BuildEntry(sagaId, 0, 1));

        // The journal table is shared with every other test in this collection, so a full null-cursor
        // scan also surfaces unrelated sagas. Restrict the "no duplicates / no omissions" check to the
        // sagaIds this test itself created.
        var seen = new List<Guid>();
        Guid? cursor = null;
        while (true)
        {
            var page = await store.EnumerateSagaIdsAsync(cursor, 2);
            if (page.Count == 0) break;
            seen.AddRange(page);
            cursor = page[^1];
        }

        var seenMine = seen.Where(sagaIds.Contains).ToList();
        Assert.Equal(sagaIds.Length, seenMine.Distinct().Count());
        foreach (var sagaId in sagaIds)
            Assert.Single(seenMine, x => x == sagaId);
    }

    [Fact]
    public async Task StepsSnapshotPayload_null_and_populated_round_trip_correctly()
    {
        var store = CreateStore();
        var sagaIdWithSteps = Guid.NewGuid();
        var sagaIdWithoutSteps = Guid.NewGuid();

        var withSteps = BuildEntry(sagaIdWithSteps, 0, 1);
        withSteps.StepsSnapshotPayload = "{\"Steps\":[{\"Name\":\"Step1\"}]}";
        var withoutSteps = BuildEntry(sagaIdWithoutSteps, 0, 1);
        withoutSteps.StepsSnapshotPayload = null;

        await store.AppendAsync(withSteps);
        await store.AppendAsync(withoutSteps);

        var readWithSteps = Assert.Single(await store.ReadAsync(sagaIdWithSteps, 0, 10));
        var readWithoutSteps = Assert.Single(await store.ReadAsync(sagaIdWithoutSteps, 0, 10));

        Assert.NotNull(readWithSteps.StepsSnapshotPayload);
        Assert.Null(readWithoutSteps.StepsSnapshotPayload);
    }

    private PostgreSqlSagaJournalStore CreateStore()
    {
        var options = new PostgreSqlSagaStoreOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        PostgreSqlSchemaMigrator.RunAsync(options).GetAwaiter().GetResult();
        PostgreSqlJournalSchemaMigrator.RunAsync(options).GetAwaiter().GetResult();
        return new PostgreSqlSagaJournalStore(options, null);
    }

    private static SagaJournalEntry BuildEntry(Guid sagaId, long previousVersion, long targetVersion) => new()
    {
        JournalEntryId = Guid.NewGuid(),
        TransitionId = Guid.NewGuid(),
        SagaId = sagaId,
        SequenceNumber = targetVersion,
        PreviousVersion = previousVersion,
        TargetVersion = targetVersion,
        TransitionType = targetVersion == 1 ? SagaJournalTransitionType.Created : SagaJournalTransitionType.Updated,
        SagaDataTypeName = "MySagaData",
        SagaDataPayload = $"{{\"SagaId\":\"{sagaId}\",\"Version\":{targetVersion}}}",
        CreatedAtUtc = DateTime.UtcNow
    };
}
