// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions.Configurations;
using Lycia.Persistence.TestKit;
using Lycia.Saga.Abstractions.Outbox;

namespace Lycia.Persistence.Redis.Tests;

[Collection(RedisSagaStoreCollection.Name)]
public class RedisOutboxStoreConformanceTests(RedisSagaStoreFixture fixture) : OutboxStoreConformanceTests
{
    protected override IOutboxStore CreateStore()
    {
        var options = new OutboxOptions
        {
            RetentionPeriod = TimeSpan.FromMinutes(5)
        };

        // Each test gets its own key namespace: the fixture's Redis container (and its "outbox:pending"
        // sorted set) is shared across every test in this class, so without isolation, one test's
        // ClaimPendingBatchAsync could observe another test's leftover pending entries.
        return new RedisOutboxStore(fixture.Database, options, $"outbox-test-{Guid.NewGuid():N}");
    }

    [Fact]
    public async Task ClaimPendingBatchAsync_Concurrent_Callers_Never_Claim_Same_Message()
    {
        var store = CreateStore();
        var messageIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in messageIds)
        {
            await store.AddAsync(new OutboxMessage(id, typeof(DummyEvent).FullName!, "{}", "TestApp", null));
        }

        var claimTasks = new[]
        {
            store.ClaimPendingBatchAsync(15),
            store.ClaimPendingBatchAsync(15)
        };

        var batches = await Task.WhenAll(claimTasks);
        var allClaimed = batches.SelectMany(b => b.Select(m => m.MessageId)).ToList();

        Assert.Equal(messageIds.Count, allClaimed.Count);
        Assert.Equal(messageIds.Count, allClaimed.Distinct().Count());
        Assert.Equal(messageIds.OrderBy(x => x), allClaimed.OrderBy(x => x));
    }

    [Fact]
    public async Task Store_Against_Unreachable_Redis_Throws_On_Operation()
    {
        await using var connection = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(
            "127.0.0.1:1,abortConnect=false,connectTimeout=200,connectRetry=0,syncTimeout=200");

        var database = connection.GetDatabase();
        var store = new RedisOutboxStore(database, new OutboxOptions());

        await Assert.ThrowsAnyAsync<StackExchange.Redis.RedisConnectionException>(() =>
            store.AddAsync(new OutboxMessage(Guid.NewGuid(), typeof(DummyEvent).FullName!, "{}", "TestApp", null)));
    }
}
