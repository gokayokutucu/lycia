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

    /// <summary>
    /// The claim script used to pop a stale Publishing entry that had reached the attempt cap and never put
    /// it back, so a row stranded by that behavior has a message record but no pending-set entry, and the
    /// script has nothing to discover. Re-adding the entry is the documented remedy for such rows; this pins
    /// that it works, and that the limitation it works around is real.
    /// </summary>
    [Fact]
    public async Task A_Row_Left_Without_A_Pending_Entry_Is_Recovered_By_Re_Adding_It()
    {
        var keyNamespace = $"outbox-test-{Guid.NewGuid():N}";
        var store = new RedisOutboxStore(fixture.Database, new OutboxOptions(), keyNamespace);
        var message = new OutboxMessage(Guid.NewGuid(), typeof(DummyEvent).FullName!, "{}", "TestApp", null);
        var window = TimeSpan.FromMilliseconds(200);
        await store.AddAsync(message);
        Assert.Contains(await store.ClaimPendingBatchAsync(100, maxAttempts: 1, recoveryTimeout: window),
            claimed => claimed.MessageId == message.MessageId);
        await store.MarkPublishingAsync(message.MessageId);

        // The shape the previous script left behind: the record survives, its pending entry does not.
        await fixture.Database.SortedSetRemoveAsync($"{keyNamespace}:pending", message.MessageId.ToString());
        await Task.Delay(600);
        Assert.DoesNotContain(await store.ClaimPendingBatchAsync(100, maxAttempts: 1, recoveryTimeout: window),
            claimed => claimed.MessageId == message.MessageId);

        await fixture.Database.SortedSetAddAsync($"{keyNamespace}:pending", message.MessageId.ToString(), 0);

        var recovered = Assert.Single(await store.ClaimPendingBatchAsync(100, maxAttempts: 1, recoveryTimeout: window));
        Assert.Equal(message.MessageId, recovered.MessageId);
        Assert.Equal(1, recovered.RetryCount);
    }
}
