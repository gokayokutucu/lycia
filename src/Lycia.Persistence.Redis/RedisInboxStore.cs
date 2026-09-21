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

    // Takes over a claim abandoned by a dead process, or retries a failed attempt whose suppression
    // window has passed, in one atomic step. Redis runs a script single-threaded, so exactly one of
    // several concurrent redeliveries can win the takeover — a read-then-write would let them all win
    // and run the handler concurrently, which is precisely what the Inbox exists to prevent.
    // Returns: 0 = took over (caller must process), 1 = already completed, 2 = still processing,
    // 3 = failed and still inside its suppression window.
    private static readonly string TakeOverScript = @"
local key = KEYS[1]
local newRecord = ARGV[1]
local staleBeforeUnixMs = tonumber(ARGV[2])
local completedStatus = tonumber(ARGV[3])
local failedStatus = tonumber(ARGV[4])
local existing = redis.call('get', key)
if not existing then
  redis.call('set', key, newRecord)
  return 0
end
local ok, decoded = pcall(cjson.decode, existing)
if not ok or not decoded then
  redis.call('set', key, newRecord)
  return 0
end
if decoded['Status'] == completedStatus then
  return 1
end
local updatedAt = tonumber(decoded['UpdatedAtUnixMs'] or 0)
if updatedAt > staleBeforeUnixMs then
  if decoded['Status'] == failedStatus then return 3 end
  return 2
end
redis.call('set', key, newRecord)
return 0";

    /// <inheritdoc />
    public async Task<InboxBeginResult> TryBeginAsync(Guid messageId, Type handlerType, CancellationToken cancellationToken = default)
    {
        var key = Key(messageId, handlerType);
        var record = InboxRecord.NewProcessing();
        var json = JsonConvert.SerializeObject(record);

        var claimed = await redisDb.StringSetAsync(key, json, when: When.NotExists);
        if (claimed) return InboxBeginResult.Started;

        // Someone already holds (or previously held) this key. A terminal Completed record means this is
        // an ordinary duplicate delivery; anything else may be a claim stranded by a crashed process, so
        // fall through to the atomic takeover check rather than skipping the work forever.
        var staleBefore = new DateTimeOffset(DateTime.UtcNow.Subtract(_options.ClaimRecoveryTimeout))
            .ToUnixTimeMilliseconds();
        var outcome = (long)await redisDb.ScriptEvaluateAsync(TakeOverScript, [key],
            [json, staleBefore, (int)InboxMessageStatus.Completed, (int)InboxMessageStatus.Failed]);

        return outcome switch
        {
            0 => InboxBeginResult.Started,
            1 => InboxBeginResult.AlreadyCompleted,
            3 => InboxBeginResult.AlreadyFailed,
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

        var record = InboxRecord.WithStatus(status, failureInfo, existing?.CreatedAtUtc);

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

        /// <summary>
        /// <see cref="UpdatedAtUtc"/> as Unix milliseconds. Stored redundantly because the claim-takeover
        /// script has to compare staleness inside Lua, where a numeric field is unambiguous and a
        /// serialized DateTime string is not.
        /// </summary>
        public long UpdatedAtUnixMs { get; set; }

        public static InboxRecord NewProcessing() => WithStatus(InboxMessageStatus.Processing, null, null);

        public static InboxRecord WithStatus(InboxMessageStatus status, SagaStepFailureInfo? failureInfo,
            DateTime? createdAtUtc)
        {
            var now = DateTime.UtcNow;
            return new InboxRecord
            {
                Status = status,
                FailureInfo = failureInfo,
                CreatedAtUtc = createdAtUtc ?? now,
                UpdatedAtUtc = now,
                UpdatedAtUnixMs = new DateTimeOffset(now).ToUnixTimeMilliseconds()
            };
        }
    }
}
