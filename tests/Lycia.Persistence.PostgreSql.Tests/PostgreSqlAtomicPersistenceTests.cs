using Lycia.Common.Enums;
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence;
using Npgsql;

namespace Lycia.Persistence.PostgreSql.Tests;

[Collection("PostgreSqlContainer")]
public class PostgreSqlAtomicPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task Shared_session_commits_inbox_saga_step_and_outbox_together()
    {
        var stores = CreateStores();
        var messageId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        await using (var session = await stores.Factory.BeginAsync())
        {
            stores.Accessor.Current = session;
            Assert.Equal(InboxBeginResult.Started,
                await stores.Inbox.TryBeginAsync(messageId, typeof(DummyEvent)));
            await stores.Saga.SaveSagaDataAsync(sagaId, new DummySagaData());
            await stores.Saga.LogStepAsync(sagaId, messageId, null, typeof(DummyEvent), StepStatus.Completed,
                typeof(PostgreSqlAtomicPersistenceTests), new DummyEvent(), (Exception?)null);
            await stores.Outbox.AddAsync(NewOutbox(messageId, sagaId));
            await stores.Inbox.MarkCompletedAsync(messageId, typeof(DummyEvent));
            await session.CommitAsync();
            stores.Accessor.Current = null;
        }

        Assert.Equal(InboxMessageStatus.Completed,
            await stores.Inbox.GetStatusAsync(messageId, typeof(DummyEvent)));
        Assert.Equal(1, (await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId)).Version);
        Assert.Equal(StepStatus.Completed,
            await stores.Saga.GetStepStatusAsync(sagaId, messageId, typeof(DummyEvent),
                typeof(PostgreSqlAtomicPersistenceTests)));
        Assert.NotNull(await stores.Outbox.GetByMessageIdAsync(messageId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Rollback_removes_every_partial_write_at_each_fault_window(int faultWindow)
    {
        var stores = CreateStores();
        var messageId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        await using (var session = await stores.Factory.BeginAsync())
        {
            stores.Accessor.Current = session;
            await stores.Inbox.TryBeginAsync(messageId, typeof(DummyEvent));
            if (faultWindow > 0)
            {
                await stores.Saga.SaveSagaDataAsync(sagaId, new DummySagaData());
                await stores.Saga.LogStepAsync(sagaId, messageId, null, typeof(DummyEvent), StepStatus.Completed,
                    typeof(PostgreSqlAtomicPersistenceTests), new DummyEvent(), (Exception?)null);
            }
            if (faultWindow > 1) await stores.Outbox.AddAsync(NewOutbox(messageId, sagaId));
            await session.RollbackAsync();
            stores.Accessor.Current = null;
        }

        Assert.Equal(InboxMessageStatus.None,
            await stores.Inbox.GetStatusAsync(messageId, typeof(DummyEvent)));
        Assert.Equal(0, (await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId)).Version);
        Assert.Null(await stores.Outbox.GetByMessageIdAsync(messageId));
    }

    [Fact]
    public void Connection_identity_ignores_order_and_secrets_but_distinguishes_database()
    {
        var first = PostgreSqlConnectionIdentity.Create(
            "Host=db;Port=5432;Database=orders;Username=one;Password=secret-one");
        var reordered = PostgreSqlConnectionIdentity.Create(
            "Password=secret-two;Database=orders;Host=db;Username=two;Port=5432");
        var different = PostgreSqlConnectionIdentity.Create(
            "Host=db;Port=5432;Database=payments;Username=one;Password=secret-one");

        Assert.Equal(first, reordered);
        Assert.NotEqual(first, different);
        Assert.DoesNotContain("secret", first, StringComparison.OrdinalIgnoreCase);
    }

    private StoreSet CreateStores()
    {
        var sagaOptions = new PostgreSqlSagaStoreOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        var inboxOptions = new PostgreSqlInboxOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        var outboxOptions = new PostgreSqlOutboxOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        PostgreSqlSchemaMigrator.RunAsync(sagaOptions).GetAwaiter().GetResult();
        PostgreSqlInboxOutboxSchemaMigrator.RunAsync(fixture.ConnectionString, "public",
            SchemaManagementMode.ApplyMigrations).GetAwaiter().GetResult();
        var accessor = new LyciaPersistenceSessionAccessor();
        return new StoreSet(
            new PostgreSqlSagaStore(sagaOptions, null!, null!, null!, sessionAccessor: accessor),
            new PostgreSqlInboxStore(inboxOptions, accessor),
            new PostgreSqlOutboxStore(outboxOptions, accessor),
            new RelationalPersistenceSessionFactory(() => new NpgsqlConnection(fixture.ConnectionString)),
            accessor);
    }

    private static OutboxMessage NewOutbox(Guid messageId, Guid sagaId) =>
        new(messageId, typeof(DummyEvent).AssemblyQualifiedName!, "{}", "atomic-tests", sagaId);

    private sealed record StoreSet(
        PostgreSqlSagaStore Saga,
        PostgreSqlInboxStore Inbox,
        PostgreSqlOutboxStore Outbox,
        RelationalPersistenceSessionFactory Factory,
        LyciaPersistenceSessionAccessor Accessor);
}
