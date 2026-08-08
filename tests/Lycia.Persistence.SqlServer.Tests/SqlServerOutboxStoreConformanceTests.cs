// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Outbox;

namespace Lycia.Persistence.SqlServer.Tests;

[Collection("SqlServerContainer")]
public class SqlServerOutboxStoreConformanceTests(SqlServerContainerFixture fixture) : OutboxStoreConformanceTests
{
    protected override IOutboxStore CreateStore()
    {
        var connectionString = fixture.ConnectionString;
        SqlServerInboxOutboxSchemaMigrator.RunAsync(connectionString, "dbo", SchemaManagementMode.ApplyMigrations)
            .GetAwaiter().GetResult();

        return new SqlServerOutboxStore(new SqlServerOutboxOptions { ConnectionString = connectionString });
    }

    [Fact]
    public async Task ClaimPendingBatchAsync_Concurrent_Callers_Never_Claim_Same_Message()
    {
        var store = CreateStore();
        const int messageCount = 20;

        var messageIds = Enumerable.Range(0, messageCount).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in messageIds)
        {
            await store.AddAsync(new OutboxMessage(id, typeof(DummyEvent).FullName!, "{}", "TestApp", null));
        }

        var claimTasks = new[]
        {
            Task.Run(() => store.ClaimPendingBatchAsync(messageCount)),
            Task.Run(() => store.ClaimPendingBatchAsync(messageCount))
        };
        var results = await Task.WhenAll(claimTasks);

        var claimedIds = results.SelectMany(batch => batch.Select(m => m.MessageId)).ToList();

        Assert.Equal(messageCount, claimedIds.Count);
        Assert.Equal(messageCount, claimedIds.Distinct().Count());
        Assert.Equal(messageIds.OrderBy(id => id), claimedIds.OrderBy(id => id));
    }

    [Fact]
    public async Task Store_With_Unreachable_Connection_Throws_On_Operation()
    {
        // Wrong port on localhost: the driver fails fast instead of hanging, and the failure surfaces
        // as a classifiable SqlException rather than being silently swallowed.
        var options = new SqlServerOutboxOptions
        {
            ConnectionString = "Server=127.0.0.1,1;Database=master;User Id=sa;Password=wrong;" +
                                "Connect Timeout=2;TrustServerCertificate=True;",
            SchemaManagement = SchemaManagementMode.Disabled
        };
        var store = new SqlServerOutboxStore(options);

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(
            () => store.GetByMessageIdAsync(Guid.NewGuid()));
    }
}
