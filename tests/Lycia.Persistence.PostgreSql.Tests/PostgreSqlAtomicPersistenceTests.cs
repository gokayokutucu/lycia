using Lycia.Common.Enums;
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
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

    [Fact]
    public async Task Shared_session_commits_canonical_state_and_reconciliation_intent_together()
    {
        var stores=CreateStores(); var sagaId=Guid.NewGuid(); var transitionId=Guid.NewGuid();
        await using(var session=await stores.Factory.BeginAsync())
        {
            stores.Accessor.Current=session;
            var data=new DummySagaData(); await stores.Saga.SaveSagaDataAsync(sagaId,data);
            await stores.Reconciliation.AddAsync(Intent(transitionId,sagaId,data.Version));
            await session.CommitAsync(); stores.Accessor.Current=null;
        }
        var claimed=await stores.Reconciliation.ClaimAsync("worker-a",10,3,TimeSpan.FromMinutes(1));
        Assert.Contains(claimed,x=>x.TransitionId==transitionId&&x.TargetVersion==1);
        var canonical = await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId);
        Assert.Equal(canonical.Version, canonical.Data?.Version);
    }

    [Fact]
    public async Task Rollback_removes_canonical_state_and_reconciliation_intent()
    {
        var stores=CreateStores(); var sagaId=Guid.NewGuid(); var transitionId=Guid.NewGuid();
        await using(var session=await stores.Factory.BeginAsync())
        {
            stores.Accessor.Current=session; var data=new DummySagaData(); await stores.Saga.SaveSagaDataAsync(sagaId,data);
            await stores.Reconciliation.AddAsync(Intent(transitionId,sagaId,data.Version)); await session.RollbackAsync(); stores.Accessor.Current=null;
        }
        Assert.Empty(await stores.Reconciliation.ClaimAsync("worker-b",10,3,TimeSpan.FromMinutes(1)));
        Assert.Equal(0,(await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId)).Version);
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
        PostgreSqlReconciliationSchemaMigrator.RunAsync(sagaOptions).GetAwaiter().GetResult();
        var accessor = new LyciaPersistenceSessionAccessor();
        return new StoreSet(
            new PostgreSqlSagaStore(sagaOptions, null!, null!, null!, sessionAccessor: accessor),
            new PostgreSqlInboxStore(inboxOptions, accessor),
            new PostgreSqlOutboxStore(outboxOptions, accessor),
            new PostgreSqlReconciliationStore(sagaOptions, accessor),
            new RelationalPersistenceSessionFactory(() => new NpgsqlConnection(fixture.ConnectionString)),
            accessor);
    }

    private static SagaProjectionIntent Intent(Guid transitionId,Guid sagaId,long version)=>new()
    {TransitionId=transitionId,SagaId=sagaId,ExpectedVersion=version-1,TargetVersion=version,SagaDataType=typeof(DummySagaData).AssemblyQualifiedName!,Payload=$"{{\"SagaId\":\"{sagaId}\",\"Version\":{version}}}",Status=ReconciliationStatus.Pending,CreatedAtUtc=DateTime.UtcNow};

    private static OutboxMessage NewOutbox(Guid messageId, Guid sagaId) =>
        new(messageId, typeof(DummyEvent).AssemblyQualifiedName!, "{}", "atomic-tests", sagaId);

    private sealed record StoreSet(
        PostgreSqlSagaStore Saga,
        PostgreSqlInboxStore Inbox,
        PostgreSqlOutboxStore Outbox,
        PostgreSqlReconciliationStore Reconciliation,
        RelationalPersistenceSessionFactory Factory,
        LyciaPersistenceSessionAccessor Accessor);
}
