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

    [Fact]
    public async Task Journal_constraint_violation_rolls_back_the_whole_transaction_including_canonical_state()
    {
        // Unlike the manual-RollbackAsync test above, this forces a genuine ADO.NET constraint
        // violation (uq_lycia_saga_journal_version on saga_id+target_version) inside AppendAsync while
        // enlisted in the same session as a canonical SagaStore save, proving the failure is not
        // silently swallowed and genuinely takes the whole processing transaction down with it.
        var stores = CreateStores();
        var sagaId = Guid.NewGuid();

        var seedTransitionId = Guid.NewGuid();
        await using (var seedSession = await stores.Factory.BeginAsync())
        {
            stores.Accessor.Current = seedSession;
            var seedData = new DummySagaData();
            await stores.Saga.SaveSagaDataAsync(sagaId, seedData);
            await stores.Journal.AppendAsync(BuildEntry(sagaId, 0, seedData.Version, seedTransitionId));
            await seedSession.CommitAsync();
            stores.Accessor.Current = null;
        }
        Assert.Equal(1, (await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId)).Version);

        await using (var session = await stores.Factory.BeginAsync())
        {
            stores.Accessor.Current = session;
            var data = new DummySagaData();
            await stores.Saga.SaveSagaDataAsync(sagaId, data); // would become version 2 if committed

            var conflictingTransitionId = Guid.NewGuid();
            await Assert.ThrowsAsync<PostgresException>(() =>
                stores.Journal.AppendAsync(BuildEntry(sagaId, 0, 1, conflictingTransitionId)));

            await session.RollbackAsync();
            stores.Accessor.Current = null;
        }

        Assert.Equal(1, (await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId)).Version);
        var entries = await stores.Journal.ReadAsync(sagaId, 0, 10);
        var entry = Assert.Single(entries);
        Assert.Equal(seedTransitionId, entry.TransitionId);
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
