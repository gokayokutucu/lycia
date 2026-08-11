using Lycia.Common.Enums;
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.Data.SqlClient;

namespace Lycia.Persistence.SqlServer.Tests;

[Collection("SqlServerContainer")]
public class SqlServerAtomicPersistenceTests(SqlServerContainerFixture fixture)
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
                typeof(SqlServerAtomicPersistenceTests), new DummyEvent(), (Exception?)null);
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
                typeof(SqlServerAtomicPersistenceTests)));
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
                    typeof(SqlServerAtomicPersistenceTests), new DummyEvent(), (Exception?)null);
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
        var first = SqlServerConnectionIdentity.Create(
            "Server=db;Initial Catalog=orders;User Id=one;Password=secret-one;TrustServerCertificate=true");
        var reordered = SqlServerConnectionIdentity.Create(
            "Password=secret-two;Database=orders;Data Source=db;User Id=two;TrustServerCertificate=true");
        var different = SqlServerConnectionIdentity.Create(
            "Server=db;Initial Catalog=payments;User Id=one;Password=secret-one;TrustServerCertificate=true");

        Assert.Equal(first, reordered);
        Assert.NotEqual(first, different);
        Assert.DoesNotContain("secret", first, StringComparison.OrdinalIgnoreCase);
    }

    private StoreSet CreateStores()
    {
        var sagaOptions = new SqlServerSagaStoreOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        var inboxOptions = new SqlServerInboxOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        var outboxOptions = new SqlServerOutboxOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };
        SqlServerSchemaMigrator.RunAsync(sagaOptions).GetAwaiter().GetResult();
        SqlServerInboxOutboxSchemaMigrator.RunAsync(fixture.ConnectionString, "dbo",
            SchemaManagementMode.ApplyMigrations).GetAwaiter().GetResult();
        var accessor = new LyciaPersistenceSessionAccessor();
        return new StoreSet(
            new SqlServerSagaStore(sagaOptions, null!, null!, null!, sessionAccessor: accessor),
            new SqlServerInboxStore(inboxOptions, accessor),
            new SqlServerOutboxStore(outboxOptions, accessor),
            new RelationalPersistenceSessionFactory(() => new SqlConnection(fixture.ConnectionString)),
            accessor);
    }

    private static OutboxMessage NewOutbox(Guid messageId, Guid sagaId) =>
        new(messageId, typeof(DummyEvent).AssemblyQualifiedName!, "{}", "atomic-tests", sagaId);

    private sealed record StoreSet(
        SqlServerSagaStore Saga,
        SqlServerInboxStore Inbox,
        SqlServerOutboxStore Outbox,
        RelationalPersistenceSessionFactory Factory,
        LyciaPersistenceSessionAccessor Accessor);
}
