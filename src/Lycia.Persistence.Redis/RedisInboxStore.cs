// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.SagaSteps;
using Lycia.Extensions;
using Lycia.Extensions.Configurations;
using Lycia.Saga.Abstractions.Inbox;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Lycia.Persistence.Redis;

/// <summary>
/// Redis-backed <see cref="IInboxStore"/>. Durable via a Redis String per (MessageId, HandlerType) pair,
/// claimed with an atomic SETNX so exactly one caller wins <see cref="TryBeginAsync"/> for a given pair.
/// </summary>
/// <remarks>
/// When <see cref="InboxOptions.RetentionPeriod"/> is set, a TTL is applied to the record's key on
/// <see cref="MarkCompletedAsync"/>/<see cref="MarkFailedAsync"/> so terminal records are cleaned up
/// automatically after that period. This is retention-cleanup only, not correctness-critical: the
/// mutual-exclusion guarantee of <see cref="TryBeginAsync"/> does not depend on the record still
/// existing, and an expired record simply behaves as if the message had never been seen before.
/// </remarks>
public class RedisInboxStore(IDatabase redisDb, InboxOptions? options) : IInboxStore
{
    private readonly InboxOptions _options = options ?? new InboxOptions();

    private static string Key(Guid messageId, Type handlerType) =>
        $"inbox:{handlerType.GetSimplifiedQualifiedName()}:{messageId}";

    /// <inheritdoc />
    public async Task<InboxBeginResult> TryBeginAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        var key = Key(messageId, handlerType);
        var record = InboxRecord.NewProcessing();
        var json = JsonConvert.SerializeObject(record);

        var claimed = await redisDb.StringSetAsync(key, json, when: When.NotExists);
        if (claimed) return InboxBeginResult.Started;

        // Another caller already holds (or previously held) this key. The atomic SETNX above already
        // decided mutual exclusion; this read only needs to report which state the loser observes.
        var existingJson = await redisDb.StringGetAsync(key);
        if (!existingJson.HasValue)
        {
            // Extremely narrow race: the winner's key expired/was removed between our failed SETNX and
            // this read. Treat as if we lost to an in-progress claim, the safest conservative answer.
            return InboxBeginResult.AlreadyProcessing;
        }

        var existing = JsonConvert.DeserializeObject<InboxRecord>(existingJson!);
        return (existing?.Status ?? InboxMessageStatus.Processing) switch
        {
            InboxMessageStatus.Completed => InboxBeginResult.AlreadyCompleted,
            InboxMessageStatus.Failed => InboxBeginResult.AlreadyFailed,
            _ => InboxBeginResult.AlreadyProcessing
        };
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default) =>
        SetStatusAsync(messageId, handlerType, InboxMessageStatus.Completed, null);

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid messageId, Type handlerType, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default) =>
        SetStatusAsync(messageId, handlerType, InboxMessageStatus.Failed, failureInfo);

    /// <inheritdoc />
    public async Task<InboxMessageStatus> GetStatusAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        var json = await redisDb.StringGetAsync(Key(messageId, handlerType));
        if (!json.HasValue) return InboxMessageStatus.None;

        var record = JsonConvert.DeserializeObject<InboxRecord>(json!);
        return record?.Status ?? InboxMessageStatus.None;
    }

    // Only the caller that won TryBeginAsync's claim is expected to call this, so a plain
    // read-modify-write (rather than CAS) is sufficient here by construction of the calling contract.
    private async Task SetStatusAsync(Guid messageId, Type handlerType, InboxMessageStatus status, SagaStepFailureInfo? failureInfo)
    {
        var key = Key(messageId, handlerType);
        var existingJson = await redisDb.StringGetAsync(key);
        var existing = existingJson.HasValue ? JsonConvert.DeserializeObject<InboxRecord>(existingJson!) : null;

        var record = new InboxRecord
        {
            Status = status,
            FailureInfo = failureInfo,
            CreatedAtUtc = existing?.CreatedAtUtc ?? DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await redisDb.StringSetAsync(key, JsonConvert.SerializeObject(record));

        if (_options.RetentionPeriod.HasValue)
            await redisDb.KeyExpireAsync(key, _options.RetentionPeriod.Value);
    }

    private class InboxRecord
    {
        public InboxMessageStatus Status { get; set; }
        public SagaStepFailureInfo? FailureInfo { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public static InboxRecord NewProcessing()
        {
            var now = DateTime.UtcNow;
            return new InboxRecord { Status = InboxMessageStatus.Processing, CreatedAtUtc = now, UpdatedAtUtc = now };
        }
    }
}
