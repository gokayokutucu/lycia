// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Outbox;
using Npgsql;

namespace Lycia.Persistence.PostgreSql.Tests;

[Collection("PostgreSqlContainer")]
public class PostgreSqlOutboxStoreConformanceTests(PostgreSqlContainerFixture fixture) : OutboxStoreConformanceTests
{
    protected override IOutboxStore CreateStore()
    {
        var options = new PostgreSqlOutboxOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaManagement = SchemaManagementMode.ApplyMigrations
        };

        PostgreSqlInboxOutboxSchemaMigrator.RunAsync(options.ConnectionString, options.SchemaName, options.SchemaManagement)
            .GetAwaiter().GetResult();

        return new PostgreSqlOutboxStore(options);
    }

    /// <summary>
    /// Exercises the FOR UPDATE SKIP LOCKED claim path: two callers race for the same pending batch and
    /// must partition it exactly, with no message claimed by both and none left unclaimed.
    /// </summary>
    [Fact]
    public async Task ClaimPendingBatchAsync_Concurrent_Callers_Never_Claim_Same_Message()
    {
        var storeA = CreateStore();
        var storeB = CreateStore();

        const int messageCount = 40;
        var messageIds = new List<Guid>();
        for (var i = 0; i < messageCount; i++)
        {
            var message = new OutboxMessage(Guid.NewGuid(), typeof(DummyEvent).FullName!, "{}", "TestApp", null);
            messageIds.Add(message.MessageId);
            await storeA.AddAsync(message);
        }

        var claimTaskA = storeA.ClaimPendingBatchAsync(messageCount);
        var claimTaskB = storeB.ClaimPendingBatchAsync(messageCount);
        var claimedBatches = await Task.WhenAll(claimTaskA, claimTaskB);

        var claimedIds = claimedBatches.SelectMany(batch => batch.Select(m => m.MessageId)).ToList();

        // The conformance suite shares one physical table across all test methods in this class
        // (unlike InMemory's per-test dictionary), so an unrelated Pending row left by another test
        // can legitimately be swept up here too. What must hold regardless: no message is ever
        // claimed by both callers, and every message this test inserted was claimed by exactly one.
        Assert.Equal(claimedIds.Count, claimedIds.Distinct().Count());
        Assert.True(messageIds.All(id => claimedIds.Contains(id)), "every inserted message must be claimed by exactly one caller");
    }

    /// <summary>
    /// A store pointed at an unreachable PostgreSQL instance must surface a clear, classifiable connection
    /// failure rather than hanging or silently swallowing the error. PostgreSqlSagaStore has no equivalent
    /// test today, so this only demonstrates the failure surfaces as a Npgsql-originated exception.
    /// </summary>
    [Fact]
    public async Task AddAsync_Against_Unreachable_Server_Throws_Npgsql_Exception()
    {
        var options = new PostgreSqlOutboxOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Database=lycia_unreachable;Username=lycia;Password=lycia;Timeout=2",
            SchemaManagement = SchemaManagementMode.Disabled
        };

        var store = new PostgreSqlOutboxStore(options);
        var message = new OutboxMessage(Guid.NewGuid(), typeof(DummyEvent).FullName!, "{}", "TestApp", null);

        await Assert.ThrowsAsync<NpgsqlException>(() => store.AddAsync(message));
    }
}
