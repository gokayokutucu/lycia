// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.SagaSteps;
using Lycia.Extensions.Configurations;
using Lycia.Saga.Abstractions.Outbox;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Lycia.Persistence.Redis;

/// <summary>
/// Redis-backed <see cref="IOutboxStore"/>. Each message is a Redis String at
/// <c>{keyNamespace}:msg:{MessageId}</c> (<c>outbox:msg:{MessageId}</c> by default) holding the full
/// <see cref="OutboxMessage"/> JSON. Pending messages are additionally tracked in the
/// <c>{keyNamespace}:pending</c> Sorted Set, scored by a monotonic sequence from
/// <c>{keyNamespace}:seq</c>, so <see cref="ClaimPendingBatchAsync"/> can claim oldest-first. The
/// <c>keyNamespace</c> constructor parameter defaults to <c>"outbox"</c> for production use; it exists so
/// multiple independent stores can share one Redis instance/database without their pending queues
/// colliding (e.g. isolated test suites).
/// </summary>
/// <remarks>
/// <para>
/// <b>TTL/retention:</b> no TTL is applied to a message purely because it left the pending set — the
/// message record persists (for <see cref="GetByMessageIdAsync"/> and audit) until it reaches a terminal
/// state. When <see cref="OutboxOptions.RetentionPeriod"/> is set, a TTL is applied to the message key on
/// <see cref="MarkPublishedAsync"/> and <see cref="MarkFailedAsync"/> only, as optional retention cleanup
/// (not correctness-critical).
/// </para>
/// <para>
/// <b>Cluster considerations:</b> <see cref="AddAsync"/> and <see cref="ClaimPendingBatchAsync"/> use
/// single Lua scripts (EVAL) spanning the <c>outbox:msg:{id}</c> key(s) and the shared <c>outbox:pending</c>
/// / <c>outbox:seq</c> keys. Redis executes a single Lua script atomically, which is what makes both
/// operations safe under concurrent callers on a standalone (non-clustered) Redis instance — but Redis
/// Cluster requires every key referenced by a single EVAL to hash to the same slot, and
/// <c>outbox:msg:{id}</c> keys do NOT hash to the same slot as <c>outbox:pending</c>/<c>outbox:seq</c> by
/// default. This implementation targets standalone/non-clustered Redis (or a single-slot-scoped
/// deployment, e.g. via a Cluster proxy). Real Redis Cluster support would require rewriting all of these
/// keys with a shared hash tag (e.g. <c>outbox:msg:{outbox}:{id}</c>, <c>{outbox}:pending</c>,
/// <c>{outbox}:seq</c>) so they co-locate to one slot; that is not done here. Beyond what a single Lua
/// script covers, Redis provides no cross-key transactional atomicity — the plain read-modify-write status
/// updates in <see cref="MarkPublishingAsync"/>/<see cref="MarkPublishedAsync"/>/
/// <see cref="MarkConfirmationUnknownAsync"/>/<see cref="MarkFailedAsync"/> are not atomic across
/// concurrent writers, which is acceptable only because a single claimed message is expected to have
/// exactly one worker acting on it post-claim.
/// </para>
/// </remarks>
public class RedisOutboxStore(IDatabase redisDb, OutboxOptions? options, string keyNamespace = "outbox") : IOutboxStore
{
    private readonly OutboxOptions _options = options ?? new OutboxOptions();

    // keyNamespace defaults to the shared "outbox" namespace for production use. Tests that need
    // multiple independently-claimable stores against one shared Redis instance (i.e. no separate
    // database per test) can pass a unique namespace per store instance to avoid the pending
    // queue/sequence colliding across otherwise-unrelated stores.
    private readonly string _pendingKey = $"{keyNamespace}:pending";
    private readonly string _seqKey = $"{keyNamespace}:seq";
    private readonly string _messageKeyPrefix = $"{keyNamespace}:msg:";

    private string MessageKey(Guid messageId) => $"{_messageKeyPrefix}{messageId}";

    // Idempotent on MessageId: only writes the message record and enqueues it into the pending set when
    // the record does not already exist, atomically, so a duplicate AddAsync is a true no-op.
    private static readonly string AddScript = @"
local msgKey = KEYS[1]
local pendingKey = KEYS[2]
local seqKey = KEYS[3]
if redis.call('exists', msgKey) == 1 then
  return 0
end
redis.call('set', msgKey, ARGV[1])
local seq = redis.call('incr', seqKey)
redis.call('zadd', pendingKey, seq, ARGV[2])
return 1";

    // Atomically pops up to maxCount oldest members off the pending set and flips each surviving
    // message's stored Status to Claimed, all inside one EVAL so two concurrent callers can never both
    // claim the same member (Redis executes a single Lua script single-threaded/atomically). A pending
    // entry whose message record no longer reports Pending (e.g. it was advanced directly, bypassing the
    // claim flow) is dropped from the queue without being claimed or returned, so the queue can never
    // hand out a message that is not actually awaiting dispatch.
    private static readonly string ClaimPendingBatchScript = @"
local pendingKey = KEYS[1]
local maxCount = tonumber(ARGV[1])
local msgPrefix = ARGV[2]
local pendingStatus = tonumber(ARGV[3])
local claimedStatus = tonumber(ARGV[4])
local results = {}
if maxCount <= 0 then
  return results
end
local ids = redis.call('zrange', pendingKey, 0, maxCount - 1)
for i = 1, #ids do
  local id = ids[i]
  if redis.call('zrem', pendingKey, id) == 1 then
    local msgKey = msgPrefix .. id
    local json = redis.call('get', msgKey)
    if json then
      local decoded = cjson.decode(json)
      if decoded['Status'] == pendingStatus then
        decoded['Status'] = claimedStatus
        local newJson = cjson.encode(decoded)
        redis.call('set', msgKey, newJson)
        table.insert(results, newJson)
      end
    end
  end
end
return results";

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        var json = JsonConvert.SerializeObject(message);
        await redisDb.ScriptEvaluateAsync(
            AddScript,
            [MessageKey(message.MessageId), _pendingKey, _seqKey],
            [json, message.MessageId.ToString()]);
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var json = await redisDb.StringGetAsync(MessageKey(messageId));
        return json.HasValue ? JsonConvert.DeserializeObject<OutboxMessage>(json!) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingBatchAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var result = (RedisResult[])(await redisDb.ScriptEvaluateAsync(
            ClaimPendingBatchScript,
            [_pendingKey],
            [maxCount, _messageKeyPrefix, (int)OutboxMessageStatus.Pending, (int)OutboxMessageStatus.Claimed]))!;

        return result
            .Select(r => JsonConvert.DeserializeObject<OutboxMessage>((string)r!)!)
            .ToList();
    }

    /// <inheritdoc />
    public Task MarkPublishingAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        SetStatusAsync(messageId, OutboxMessageStatus.Publishing, setFailureInfo: false, null, applyRetentionTtl: false);

    /// <inheritdoc />
    public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        SetStatusAsync(messageId, OutboxMessageStatus.Published, setFailureInfo: false, null, applyRetentionTtl: true);

    /// <inheritdoc />
    public Task MarkConfirmationUnknownAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        SetStatusAsync(messageId, OutboxMessageStatus.ConfirmationUnknown, setFailureInfo: false, null, applyRetentionTtl: false);

    /// <inheritdoc />
    public Task MarkFailedAsync(Guid messageId, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default) =>
        SetStatusAsync(messageId, OutboxMessageStatus.Failed, setFailureInfo: true, failureInfo, applyRetentionTtl: true);

    // Only the single worker that owns a message post-claim is expected to call these, so a plain
    // read-modify-write (rather than a CAS/Lua script) is sufficient here by construction of the
    // calling contract.
    private async Task SetStatusAsync(Guid messageId, OutboxMessageStatus status, bool setFailureInfo, SagaStepFailureInfo? failureInfo, bool applyRetentionTtl)
    {
        var key = MessageKey(messageId);
        var json = await redisDb.StringGetAsync(key);
        if (!json.HasValue) return;

        var message = JsonConvert.DeserializeObject<OutboxMessage>(json!)!;
        message.Status = status;
        if (setFailureInfo) message.FailureInfo = failureInfo;
        message.UpdatedAtUtc = DateTime.UtcNow;

        await redisDb.StringSetAsync(key, JsonConvert.SerializeObject(message));

        if (applyRetentionTtl && _options.RetentionPeriod.HasValue)
            await redisDb.KeyExpireAsync(key, _options.RetentionPeriod.Value);
    }
}
