// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Messaging;
using Lycia.Saga.Abstractions.Scheduling;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Lycia.Extensions.Scheduling;

/// <summary>Redis-backed schedule store with atomic claiming, expiring leases, and fencing tokens.</summary>
public sealed class RedisScheduleStore : IScheduleStore
{
    private readonly IDatabase _database;
    private readonly string _prefix;
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        DateParseHandling = DateParseHandling.DateTimeOffset
    };

    /// <summary>Creates a store scoped to the normalized logical application identity.</summary>
    public RedisScheduleStore(IConnectionMultiplexer connection, string applicationId)
    {
        if (connection == null) throw new ArgumentNullException(nameof(connection));
        _database = connection.GetDatabase();
        _prefix = "lycia:scheduling:" + EndpointIdentityNormalizer.Default.Normalize(applicationId);
    }

    /// <inheritdoc />
    public async Task<ScheduleCreationResult> CreateAsync(ScheduleRecord record,
        CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        cancellationToken.ThrowIfCancellationRequested();
        var script = @"
if redis.call('EXISTS', KEYS[1]) == 1 then return 0 end
redis.call('HSET', KEYS[1],
  'json', ARGV[1], 'status', ARGV[2], 'due', ARGV[3], 'attempt', 0,
  'fence', 0, 'destination', ARGV[5], 'resource', ARGV[6])
redis.call('ZADD', KEYS[2], ARGV[3], ARGV[4])
redis.call('SADD', KEYS[3], ARGV[4])
if ARGV[6] ~= '' then redis.call('SADD', KEYS[4], ARGV[4]) end
return 1";
        var result = (long)await _database.ScriptEvaluateAsync(script,
            new RedisKey[] { EntryKey(record.ScheduleId), DueKey, DestinationKey(record.Destination), ResourceKey(record.CreatedResourceId) },
            new RedisValue[]
            {
                JsonConvert.SerializeObject(record, _jsonSettings), record.Status.ToString(), ToUnixMs(record.DueAtUtc),
                record.ScheduleId.ToString("D"), record.Destination, record.CreatedResourceId ?? string.Empty
            }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result == 0)
        {
            var existing = await GetAsync(record.ScheduleId, cancellationToken).ConfigureAwait(false)
                           ?? throw new InvalidOperationException($"ScheduleId '{record.ScheduleId}' exists without a readable record.");
            EnsureSameRequest(existing, record);
        }
        return new ScheduleCreationResult { ScheduleId = record.ScheduleId, Created = result == 1 };
    }

    /// <inheritdoc />
    public async Task<ScheduleRecord?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = await _database.HashGetAsync(EntryKey(scheduleId),
            new RedisValue[] { "json", "status", "attempt", "next", "owner", "lease", "fence", "error", "completed", "resource", "strategy", "due" })
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (values[0].IsNullOrEmpty) return null;
        var record = JsonConvert.DeserializeObject<ScheduleRecord>(values[0]!, _jsonSettings)
                     ?? throw new InvalidOperationException($"ScheduleId '{scheduleId}' contains invalid JSON.");
        ApplyMutableFields(record, values);
        return record;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduleClaim>> ClaimDueAsync(DateTimeOffset nowUtc, int maximumCount,
        string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (string.IsNullOrWhiteSpace(leaseOwner)) throw new ArgumentException("Lease owner is required.", nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        cancellationToken.ThrowIfCancellationRequested();
        var script = @"
local ids = redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', ARGV[1], 'LIMIT', 0, ARGV[2])
local result = {}
for _, id in ipairs(ids) do
  local key = ARGV[5] .. id
  local status = redis.call('HGET', key, 'status')
  local lease = tonumber(redis.call('HGET', key, 'lease') or '0')
  if status == 'Pending' or status == 'RetryPending' or
     ((status == 'Claimed' or status == 'Dispatching') and lease <= tonumber(ARGV[1])) then
    local fence = redis.call('HINCRBY', key, 'fence', 1)
    redis.call('HSET', key, 'status', 'Claimed', 'owner', ARGV[3], 'lease', ARGV[4])
    redis.call('ZADD', KEYS[1], ARGV[4], id)
    table.insert(result, id)
    table.insert(result, tostring(fence))
  end
end
return result";
        var now = ToUnixMs(nowUtc);
        var leaseUntil = ToUnixMs(nowUtc.ToUniversalTime().Add(leaseDuration));
        var raw = (RedisResult[]?)await _database.ScriptEvaluateAsync(script,
            new RedisKey[] { DueKey },
            new RedisValue[] { now, maximumCount, leaseOwner, leaseUntil, _prefix + ":entry:" })
            .ConfigureAwait(false) ?? Array.Empty<RedisResult>();
        var claims = new List<ScheduleClaim>(raw.Length / 2);
        for (var index = 0; index + 1 < raw.Length; index += 2)
        {
            var id = Guid.Parse((string)raw[index]!);
            var fence = long.Parse((string)raw[index + 1]!, System.Globalization.CultureInfo.InvariantCulture);
            var record = await GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (record == null) continue;
            claims.Add(new ScheduleClaim { Record = record, LeaseOwner = leaseOwner, FencingToken = fence });
        }
        return claims;
    }

    /// <inheritdoc />
    public Task<bool> RenewLeaseAsync(Guid scheduleId, string leaseOwner, long fencingToken,
        DateTimeOffset leaseUntilUtc, CancellationToken cancellationToken = default) =>
        EvaluateOwnedMutationAsync(scheduleId, leaseOwner, fencingToken,
            "redis.call('HSET', KEYS[1], 'lease', ARGV[3]); redis.call('ZADD', KEYS[2], ARGV[3], ARGV[4])",
            new RedisValue[] { ToUnixMs(leaseUntilUtc), scheduleId.ToString("D") }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> MarkDispatchingAsync(Guid scheduleId, string leaseOwner, long fencingToken,
        CancellationToken cancellationToken = default) =>
        EvaluateOwnedMutationAsync(scheduleId, leaseOwner, fencingToken,
            "if redis.call('HGET', KEYS[1], 'status') ~= 'Claimed' then return 0 end; redis.call('HSET', KEYS[1], 'status', 'Dispatching')",
            Array.Empty<RedisValue>(), cancellationToken);

    /// <inheritdoc />
    public Task<bool> CompleteAsync(Guid scheduleId, string leaseOwner, long fencingToken,
        DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default) =>
        EvaluateOwnedMutationAsync(scheduleId, leaseOwner, fencingToken,
            "if redis.call('HGET', KEYS[1], 'status') ~= 'Dispatching' then return 0 end; " +
            "redis.call('HSET', KEYS[1], 'status', 'Completed', 'completed', ARGV[3], 'owner', '', 'lease', ''); " +
            "redis.call('ZREM', KEYS[2], ARGV[4])",
            new RedisValue[] { ToUnixMs(completedAtUtc), scheduleId.ToString("D") }, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> CompleteNativeAsync(Guid scheduleId, string? resourceId, SchedulingStrategy strategy,
        DateTimeOffset acceptedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var script = @"
local status = redis.call('HGET', KEYS[1], 'status')
if status == 'Completed' then return 1 end
if status ~= 'Pending' then return 0 end
redis.call('HSET', KEYS[1], 'status', 'Completed', 'completed', ARGV[1], 'resource', ARGV[2], 'strategy', ARGV[3])
redis.call('ZREM', KEYS[2], ARGV[4])
if ARGV[2] ~= '' then redis.call('SADD', KEYS[3], ARGV[4]) end
return 1";
        var result = (long)await _database.ScriptEvaluateAsync(script,
            new RedisKey[] { EntryKey(scheduleId), DueKey, ResourceKey(resourceId) },
            new RedisValue[] { ToUnixMs(acceptedAtUtc), resourceId ?? string.Empty, strategy.ToString(), scheduleId.ToString("D") })
            .ConfigureAwait(false);
        return result == 1;
    }

    /// <inheritdoc />
    public Task<bool> FailAsync(Guid scheduleId, string leaseOwner, long fencingToken, string error,
        DateTimeOffset? retryAtUtc, CancellationToken cancellationToken = default)
    {
        var status = retryAtUtc.HasValue ? ScheduleStatus.RetryPending : ScheduleStatus.Failed;
        var score = retryAtUtc.HasValue ? ToUnixMs(retryAtUtc.Value) : 0L;
        var body = "redis.call('HINCRBY', KEYS[1], 'attempt', 1); " +
                   "redis.call('HSET', KEYS[1], 'status', ARGV[3], 'error', ARGV[4], 'next', ARGV[5], 'owner', '', 'lease', ''); " +
                   (retryAtUtc.HasValue
                       ? "redis.call('ZADD', KEYS[2], ARGV[5], ARGV[6])"
                       : "redis.call('ZREM', KEYS[2], ARGV[6])");
        return EvaluateOwnedMutationAsync(scheduleId, leaseOwner, fencingToken, body,
            new RedisValue[] { status.ToString(), error, score, scheduleId.ToString("D") }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var script = @"
local status = redis.call('HGET', KEYS[1], 'status')
if not status then return 0 end
if status == 'Cancelled' then return 1 end
if status == 'Completed' or status == 'Dispatching' then return 0 end
redis.call('HSET', KEYS[1], 'status', 'Cancelled', 'owner', '', 'lease', '')
redis.call('ZREM', KEYS[2], ARGV[1])
return 1";
        return (long)await _database.ScriptEvaluateAsync(script,
            new RedisKey[] { EntryKey(scheduleId), DueKey }, new RedisValue[] { scheduleId.ToString("D") })
            .ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public async Task<bool> RescheduleAsync(Guid scheduleId, DateTimeOffset dueAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var script = @"
local status = redis.call('HGET', KEYS[1], 'status')
if not status or status == 'Completed' or status == 'Dispatching' or status == 'Failed' then return 0 end
redis.call('HSET', KEYS[1], 'status', 'Pending', 'due', ARGV[1], 'next', '', 'owner', '', 'lease', '')
redis.call('ZADD', KEYS[2], ARGV[1], ARGV[2])
return 1";
        return (long)await _database.ScriptEvaluateAsync(script,
            new RedisKey[] { EntryKey(scheduleId), DueKey },
            new RedisValue[] { ToUnixMs(dueAtUtc), scheduleId.ToString("D") }).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public Task<long> CountActiveByResourceAsync(string resourceId, CancellationToken cancellationToken = default) =>
        CountActiveAsync(ResourceKey(resourceId), cancellationToken);

    /// <inheritdoc />
    public Task<long> CountActiveByDestinationAsync(string destination, CancellationToken cancellationToken = default) =>
        CountActiveAsync(DestinationKey(destination), cancellationToken);

    private async Task<long> CountActiveAsync(RedisKey setKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var script = @"
local ids = redis.call('SMEMBERS', KEYS[1])
local count = 0
for _, id in ipairs(ids) do
  local status = redis.call('HGET', ARGV[1] .. id, 'status')
  if status == 'Pending' or status == 'Claimed' or status == 'Dispatching' or status == 'RetryPending' then count = count + 1 end
end
return count";
        return (long)await _database.ScriptEvaluateAsync(script, new RedisKey[] { setKey },
            new RedisValue[] { _prefix + ":entry:" }).ConfigureAwait(false);
    }

    private async Task<bool> EvaluateOwnedMutationAsync(Guid scheduleId, string leaseOwner, long fencingToken,
        string mutation, RedisValue[] extraArguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var script = "if redis.call('HGET', KEYS[1], 'owner') ~= ARGV[1] or tonumber(redis.call('HGET', KEYS[1], 'fence') or '-1') ~= tonumber(ARGV[2]) then return 0 end; " +
                     mutation + "; return 1";
        var args = new RedisValue[2 + extraArguments.Length];
        args[0] = leaseOwner;
        args[1] = fencingToken;
        Array.Copy(extraArguments, 0, args, 2, extraArguments.Length);
        return (long)await _database.ScriptEvaluateAsync(script,
            new RedisKey[] { EntryKey(scheduleId), DueKey }, args).ConfigureAwait(false) == 1;
    }

    private static void ApplyMutableFields(ScheduleRecord record, RedisValue[] values)
    {
        if (Enum.TryParse((string?)values[1], out ScheduleStatus status)) record.Status = status;
        if (values[2].TryParse(out long attempt)) record.AttemptCount = checked((int)attempt);
        record.NextAttemptAtUtc = FromUnixMs(values[3]);
        record.LeaseOwner = values[4].IsNullOrEmpty ? null : (string?)values[4];
        record.LeaseUntilUtc = FromUnixMs(values[5]);
        if (values[6].TryParse(out long fence)) record.FencingToken = fence;
        record.LastError = values[7].IsNullOrEmpty ? null : (string?)values[7];
        record.CompletedAtUtc = FromUnixMs(values[8]);
        record.CreatedResourceId = values[9].IsNullOrEmpty ? record.CreatedResourceId : (string?)values[9];
        if (Enum.TryParse((string?)values[10], out SchedulingStrategy strategy)) record.Strategy = strategy;
        var dueAtUtc = FromUnixMs(values[11]);
        if (dueAtUtc.HasValue) record.DueAtUtc = dueAtUtc.Value;
    }

    private static void EnsureSameRequest(ScheduleRecord existing, ScheduleRecord candidate)
    {
        if (existing.MessageId != candidate.MessageId ||
            existing.MessageKind != candidate.MessageKind ||
            !string.Equals(existing.MessageType, candidate.MessageType, StringComparison.Ordinal) ||
            !string.Equals(existing.Destination, candidate.Destination, StringComparison.Ordinal) ||
            !string.Equals(existing.IdempotencyKey, candidate.IdempotencyKey, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"ScheduleId '{candidate.ScheduleId}' is already associated with a different scheduling request.");
    }

    private RedisKey EntryKey(Guid id) => _prefix + ":entry:" + id.ToString("D");
    private RedisKey DueKey => _prefix + ":due";
    private RedisKey DestinationKey(string destination) => _prefix + ":destination:" + destination;
    private RedisKey ResourceKey(string? resourceId) => _prefix + ":resource:" + (resourceId ?? "none");
    private static long ToUnixMs(DateTimeOffset value) => value.ToUniversalTime().ToUnixTimeMilliseconds();
    private static DateTimeOffset? FromUnixMs(RedisValue value) =>
        value.TryParse(out long milliseconds) && milliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;
}
