using Lycia.Common.Enums;
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.Relational.Internal.Sessions;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
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

    [Fact]
    public async Task Shared_session_commits_canonical_state_and_reconciliation_intent_together()
    {
        var stores=CreateStores(); var sagaId=Guid.NewGuid(); var transitionId=Guid.NewGuid();
        await using(var session=await stores.Factory.BeginAsync())
        { stores.Accessor.Current=session; var data=new DummySagaData(); await stores.Saga.SaveSagaDataAsync(sagaId,data);
          await stores.Reconciliation.AddAsync(Intent(transitionId,sagaId,data.Version)); await session.CommitAsync(); stores.Accessor.Current=null; }
        Assert.Contains(await stores.Reconciliation.ClaimAsync("worker-a",10,3,TimeSpan.FromMinutes(1)),x=>x.TransitionId==transitionId&&x.TargetVersion==1);
        var canonical = await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId);
        Assert.Equal(canonical.Version, canonical.Data?.Version);
    }

    [Fact]
    public async Task Rollback_removes_canonical_state_and_reconciliation_intent()
    {
        var stores=CreateStores(); var sagaId=Guid.NewGuid(); var transitionId=Guid.NewGuid();
        await using(var session=await stores.Factory.BeginAsync())
        { stores.Accessor.Current=session; var data=new DummySagaData(); await stores.Saga.SaveSagaDataAsync(sagaId,data);
          await stores.Reconciliation.AddAsync(Intent(transitionId,sagaId,data.Version)); await session.RollbackAsync(); stores.Accessor.Current=null; }
        Assert.Empty(await stores.Reconciliation.ClaimAsync("worker-b",10,3,TimeSpan.FromMinutes(1)));
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
    public async Task Shared_session_commits_canonical_state_and_journal_entry_together()
    {
        var stores = CreateStores();
        var sagaId = Guid.NewGuid();
        var transitionId = Guid.NewGuid();
        await using (var session = await stores.Factory.BeginAsync())
        {
            stores.Accessor.Current = session;
            var data = new DummySagaData();
            await stores.Saga.SaveSagaDataAsync(sagaId, data);
            await stores.Journal.AppendAsync(NewJournalEntry(transitionId, sagaId, 0, data.Version));
            await session.CommitAsync();
            stores.Accessor.Current = null;
        }

        var entries = await stores.Journal.ReadAsync(sagaId, 0, 10);
        var entry = Assert.Single(entries);
        Assert.Equal(transitionId, entry.TransitionId);
        Assert.Equal(1, await stores.Journal.GetLatestVersionAsync(sagaId));
        var canonical = await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId);
        Assert.Equal(1, canonical.Version);
    }

    [Fact]
    public async Task Rollback_leaves_no_phantom_journal_history()
    {
        var stores = CreateStores();
        var sagaId = Guid.NewGuid();
        var transitionId = Guid.NewGuid();
        await using (var session = await stores.Factory.BeginAsync())
        {
            stores.Accessor.Current = session;
            var data = new DummySagaData();
            await stores.Saga.SaveSagaDataAsync(sagaId, data);
            await stores.Journal.AppendAsync(NewJournalEntry(transitionId, sagaId, 0, data.Version));
            await session.RollbackAsync();
            stores.Accessor.Current = null;
        }

        Assert.Empty(await stores.Journal.ReadAsync(sagaId, 0, 10));
        Assert.Equal(0, await stores.Journal.GetLatestVersionAsync(sagaId));
        Assert.Equal(0, (await stores.Saga.LoadSagaDataWithVersionAsync<DummySagaData>(sagaId)).Version);
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
        SqlServerReconciliationSchemaMigrator.RunAsync(sagaOptions).GetAwaiter().GetResult();
        SqlServerJournalSchemaMigrator.RunAsync(sagaOptions).GetAwaiter().GetResult();
        var accessor = new LyciaPersistenceSessionAccessor();
        return new StoreSet(
            new SqlServerSagaStore(sagaOptions, null!, null!, null!, sessionAccessor: accessor),
            new SqlServerInboxStore(inboxOptions, accessor),
            new SqlServerOutboxStore(outboxOptions, accessor),
            new SqlServerReconciliationStore(sagaOptions, accessor),
            new SqlServerSagaJournalStore(sagaOptions, accessor),
            new RelationalPersistenceSessionFactory(() => new SqlConnection(fixture.ConnectionString)),
            accessor);
    }

    private static SagaProjectionIntent Intent(Guid transitionId,Guid sagaId,long version)=>new()
    {TransitionId=transitionId,SagaId=sagaId,ExpectedVersion=version-1,TargetVersion=version,SagaDataType=typeof(DummySagaData).AssemblyQualifiedName!,Payload=$"{{\"SagaId\":\"{sagaId}\",\"Version\":{version}}}",Status=ReconciliationStatus.Pending,CreatedAtUtc=DateTime.UtcNow};

    private static SagaJournalEntry NewJournalEntry(Guid transitionId, Guid sagaId, long previousVersion, long targetVersion) => new()
    {
        JournalEntryId = Guid.NewGuid(),
        TransitionId = transitionId,
        SagaId = sagaId,
        SequenceNumber = targetVersion,
        PreviousVersion = previousVersion,
        TargetVersion = targetVersion,
        TransitionType = targetVersion == 1 ? SagaJournalTransitionType.Created : SagaJournalTransitionType.Updated,
        SagaDataTypeName = typeof(DummySagaData).AssemblyQualifiedName!,
        SagaDataPayload = $"{{\"SagaId\":\"{sagaId}\",\"Version\":{targetVersion}}}",
        CreatedAtUtc = DateTime.UtcNow
    };

    private static OutboxMessage NewOutbox(Guid messageId, Guid sagaId) =>
        new(messageId, typeof(DummyEvent).AssemblyQualifiedName!, "{}", "atomic-tests", sagaId);

    private sealed record StoreSet(
        SqlServerSagaStore Saga,
        SqlServerInboxStore Inbox,
        SqlServerOutboxStore Outbox,
        SqlServerReconciliationStore Reconciliation,
        SqlServerSagaJournalStore Journal,
        RelationalPersistenceSessionFactory Factory,
        LyciaPersistenceSessionAccessor Accessor);
}
