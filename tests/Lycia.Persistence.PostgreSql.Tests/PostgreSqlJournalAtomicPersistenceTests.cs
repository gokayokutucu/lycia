// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Npgsql;

namespace Lycia.Persistence.PostgreSql.Tests;

/// <summary>
/// Proves the journal append participates in the same LocalAtomic canonical transaction as the
/// SagaStore save: a shared-session commit persists both, and a shared-session rollback leaves no
/// phantom journal row behind.
/// </summary>
[Collection("PostgreSqlContainer")]
public class PostgreSqlJournalAtomicPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task Shared_session_commits_canonical_saga_data_and_journal_entry_together()
    {
        var stores = CreateStores();
        var sagaId = Guid.NewGuid();

        await using (var session = await stores.Factory.BeginAsync())
        {
            stores.Accessor.Current = session;
            var data = new DummySagaData();
            await stores.Saga.SaveSagaDataAsync(sagaId, data);
            await stores.Journal.AppendAsync(BuildEntry(sagaId, 0, data.Version));
            await session.CommitAsync();
            stores.Accessor.Current = null;
        }

        var journalEntries = await stores.Journal.ReadAsync(sagaId, 0, 10);
        Assert.Single(journalEntries);
        Assert.Equal(1, await stores.Journal.GetLatestVersionAsync(sagaId));
        Assert.Equal(1, (await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId)).Version);
    }

    [Fact]
    public async Task Rollback_leaves_no_phantom_journal_row()
    {
        var stores = CreateStores();
        var sagaId = Guid.NewGuid();
        var transitionId = Guid.NewGuid();

        await using (var session = await stores.Factory.BeginAsync())
        {
            stores.Accessor.Current = session;
            var data = new DummySagaData();
            await stores.Saga.SaveSagaDataAsync(sagaId, data);
            await stores.Journal.AppendAsync(BuildEntry(sagaId, 0, data.Version, transitionId));
            await session.RollbackAsync();
            stores.Accessor.Current = null;
        }

        var journalEntries = await stores.Journal.ReadAsync(sagaId, 0, 10);
        Assert.Empty(journalEntries);
        Assert.Equal(0, await stores.Journal.GetLatestVersionAsync(sagaId));
        Assert.Equal(0, (await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId)).Version);

        // Directly confirm no row exists for this transition id, independent of ReadAsync's own filtering.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM lycia_saga_journal WHERE transition_id = @id;";
        command.Parameters.AddWithValue("id", transitionId);
        var count = (long)(await command.ExecuteScalarAsync())!;
        Assert.Equal(0, count);
    }

    private StoreSet CreateStores()
    {
        var sagaOptions = new PostgreSqlSagaStoreOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        PostgreSqlSchemaMigrator.RunAsync(sagaOptions).GetAwaiter().GetResult();
        PostgreSqlJournalSchemaMigrator.RunAsync(sagaOptions).GetAwaiter().GetResult();
        var accessor = new LyciaPersistenceSessionAccessor();
        return new StoreSet(
            new PostgreSqlSagaStore(sagaOptions, null!, null!, null!, sessionAccessor: accessor),
            new PostgreSqlSagaJournalStore(sagaOptions, accessor),
            new RelationalPersistenceSessionFactory(() => new NpgsqlConnection(fixture.ConnectionString)),
            accessor);
    }

    private static SagaJournalEntry BuildEntry(Guid sagaId, long previousVersion, long targetVersion, Guid? transitionId = null) => new()
    {
        JournalEntryId = Guid.NewGuid(),
        TransitionId = transitionId ?? Guid.NewGuid(),
        SagaId = sagaId,
        SequenceNumber = targetVersion,
        PreviousVersion = previousVersion,
        TargetVersion = targetVersion,
        TransitionType = targetVersion == 1 ? SagaJournalTransitionType.Created : SagaJournalTransitionType.Updated,
        SagaDataTypeName = typeof(DummySagaData).AssemblyQualifiedName!,
        SagaDataPayload = $"{{\"SagaId\":\"{sagaId}\",\"Version\":{targetVersion}}}",
        CreatedAtUtc = DateTime.UtcNow
    };

    private sealed record StoreSet(
        PostgreSqlSagaStore Saga,
        PostgreSqlSagaJournalStore Journal,
        RelationalPersistenceSessionFactory Factory,
        LyciaPersistenceSessionAccessor Accessor);
}
