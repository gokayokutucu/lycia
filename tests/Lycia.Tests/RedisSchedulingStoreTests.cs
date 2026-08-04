// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Extensions.Scheduling;
using Lycia.Saga.Abstractions.Scheduling;

namespace Lycia.Tests;

[Collection(RedisSagaStoreCollection.Name)]
public sealed class RedisSchedulingStoreTests(RedisSagaStoreFixture fixture)
{
    [Fact]
    public async Task Redis_claim_is_atomic_across_replicas_and_recovers_with_a_new_fence()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new RedisScheduleStore(fixture.Connection, "Scheduling Test " + Guid.NewGuid());
        var record = CreateRecord(now);
        await store.CreateAsync(record);

        var claims = await Task.WhenAll(
            store.ClaimDueAsync(now, 1, "replica-a", TimeSpan.FromSeconds(2)),
            store.ClaimDueAsync(now, 1, "replica-b", TimeSpan.FromSeconds(2)));
        var first = Assert.Single(claims.SelectMany(value => value));
        Assert.Empty(claims.Single(value => value.Count == 0));

        var recovered = Assert.Single(await store.ClaimDueAsync(now.AddSeconds(3), 1, "replica-c",
            TimeSpan.FromSeconds(2)));
        Assert.True(recovered.FencingToken > first.FencingToken);
        Assert.False(await store.MarkDispatchingAsync(record.ScheduleId, first.LeaseOwner, first.FencingToken));
        Assert.True(await store.MarkDispatchingAsync(record.ScheduleId, recovered.LeaseOwner, recovered.FencingToken));
        Assert.True(await store.CompleteAsync(record.ScheduleId, recovered.LeaseOwner, recovered.FencingToken,
            now.AddSeconds(4)));
    }

    [Fact]
    public async Task Redis_schedule_creation_is_idempotent_for_the_same_stable_intent()
    {
        var store = new RedisScheduleStore(fixture.Connection, "Scheduling Test " + Guid.NewGuid());
        var record = CreateRecord(DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.True((await store.CreateAsync(record)).Created);
        var retry = CreateRecord(record.DueAtUtc.AddSeconds(5));
        retry.ScheduleId = record.ScheduleId;
        retry.MessageId = record.MessageId;
        Assert.False((await store.CreateAsync(retry)).Created);
        Assert.Equal(record.DueAtUtc.ToUnixTimeMilliseconds(),
            (await store.GetAsync(record.ScheduleId))!.DueAtUtc.ToUnixTimeMilliseconds());
    }

    private static ScheduleRecord CreateRecord(DateTimeOffset dueAtUtc) => new()
    {
        ScheduleId = Guid.NewGuid(),
        MessageId = Guid.NewGuid(),
        MessageType = typeof(SchedulingTests.TestEvent).AssemblyQualifiedName!,
        MessageKind = ScheduledMessageKind.Event,
        Destination = "testapplication",
        DueAtUtc = dueAtUtc,
        ScheduledAtUtc = dueAtUtc.AddMinutes(-1),
        Status = ScheduleStatus.Pending,
        Payload = [1],
        DelaySuffix = "1m",
        IdempotencyKey = "delay:1m"
    };
}
