// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Scheduling;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Lycia.Extensions.Scheduling;

/// <summary>Redis-backed provenance registry for Lycia-created scheduling resources.</summary>
public sealed class RedisSchedulingResourceRegistry(IConnectionMultiplexer connection) : ISchedulingResourceRegistry
{
    private readonly IDatabase _database = connection.GetDatabase();
    private const string IndexKey = "lycia:scheduling:resources";

    /// <inheritdoc />
    public async Task UpsertAsync(SchedulingResourceRecord resource, CancellationToken cancellationToken = default)
    {
        Validate(resource);
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = _database.CreateTransaction();
        _ = transaction.StringSetAsync(Key(resource.ResourceId), JsonConvert.SerializeObject(resource));
        _ = transaction.SortedSetAddAsync(IndexKey, resource.ResourceId, resource.LastUsedAtUtc.ToUnixTimeMilliseconds());
        await transaction.ExecuteAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SchedulingResourceRecord?> GetAsync(string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await _database.StringGetAsync(Key(resourceId)).ConfigureAwait(false);
        return json.IsNullOrEmpty ? null : JsonConvert.DeserializeObject<SchedulingResourceRecord>(json!);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SchedulingResourceRecord>> ListCandidatesAsync(int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        cancellationToken.ThrowIfCancellationRequested();
        var ids = await _database.SortedSetRangeByRankAsync(IndexKey, 0, maximumCount - 1).ConfigureAwait(false);
        var result = new List<SchedulingResourceRecord>(ids.Length);
        foreach (var id in ids)
        {
            if (id.IsNullOrEmpty) continue;
            var resource = await GetAsync(id!, cancellationToken).ConfigureAwait(false);
            if (resource != null && resource.Lifecycle != SchedulingResourceLifecycle.Deleted) result.Add(resource);
        }
        return result;
    }

    /// <inheritdoc />
    public Task UpdateAsync(SchedulingResourceRecord resource, CancellationToken cancellationToken = default) =>
        UpsertAsync(resource, cancellationToken);

    private static RedisKey Key(string id) => "lycia:scheduling:resource:" + id;
    private static void Validate(SchedulingResourceRecord resource)
    {
        if (resource == null) throw new ArgumentNullException(nameof(resource));
        if (string.IsNullOrWhiteSpace(resource.ResourceId)) throw new ArgumentException("ResourceId is required.", nameof(resource));
    }
}

/// <summary>Redis-backed topology heartbeat registry that keeps replica identity out of routing names.</summary>
public sealed class RedisTopologyManifestRegistry(IConnectionMultiplexer connection) : ITopologyManifestRegistry
{
    private readonly IDatabase _database = connection.GetDatabase();
    private const string IndexKey = "lycia:topology:manifests";

    /// <inheritdoc />
    public async Task HeartbeatAsync(TopologyManifest manifest, CancellationToken cancellationToken = default)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        cancellationToken.ThrowIfCancellationRequested();
        var id = Id(manifest);
        var transaction = _database.CreateTransaction();
        _ = transaction.StringSetAsync(Key(id), JsonConvert.SerializeObject(manifest), TimeSpan.FromMinutes(10));
        _ = transaction.SortedSetAddAsync(IndexKey, id, manifest.LastHeartbeatAtUtc.ToUnixTimeMilliseconds());
        await transaction.ExecuteAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TopologyManifest>> GetActiveAsync(DateTimeOffset nowUtc, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        cancellationToken.ThrowIfCancellationRequested();
        var cutoff = nowUtc.Subtract(timeout).ToUnixTimeMilliseconds();
        var ids = await _database.SortedSetRangeByScoreAsync(IndexKey, cutoff, double.PositiveInfinity)
            .ConfigureAwait(false);
        var result = new List<TopologyManifest>(ids.Length);
        foreach (var id in ids)
        {
            var json = await _database.StringGetAsync(Key(id!)).ConfigureAwait(false);
            if (json.IsNullOrEmpty) continue;
            var manifest = JsonConvert.DeserializeObject<TopologyManifest>(json!);
            if (manifest != null) result.Add(manifest);
        }
        return result;
    }

    private static string Id(TopologyManifest manifest) =>
        manifest.CanonicalApplicationKey + ":" + manifest.DeploymentId + ":" + manifest.InstanceId;
    private static RedisKey Key(string id) => "lycia:topology:manifest:" + id;
}

/// <summary>Redis distributed lease implementation with monotonic fencing tokens.</summary>
public sealed class RedisVacuumLeaseManager(IConnectionMultiplexer connection) : IVacuumLeaseManager
{
    private readonly IDatabase _database = connection.GetDatabase();

    /// <inheritdoc />
    public async Task<long?> TryAcquireAsync(string scope, string owner, DateTimeOffset nowUtc, TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var script = @"
local currentOwner = redis.call('HGET', KEYS[1], 'owner')
local untilMs = tonumber(redis.call('HGET', KEYS[1], 'until') or '0')
if currentOwner and currentOwner ~= ARGV[1] and untilMs > tonumber(ARGV[2]) then return nil end
local fence = redis.call('INCR', KEYS[2])
redis.call('HSET', KEYS[1], 'owner', ARGV[1], 'until', ARGV[3], 'fence', fence)
redis.call('PEXPIRE', KEYS[1], ARGV[4])
return fence";
        var durationMs = checked((long)Math.Ceiling(duration.TotalMilliseconds));
        var result = await _database.ScriptEvaluateAsync(script,
            new RedisKey[] { LeaseKey(scope), FenceKey(scope) },
            new RedisValue[] { owner, nowUtc.ToUnixTimeMilliseconds(), nowUtc.Add(duration).ToUnixTimeMilliseconds(), durationMs })
            .ConfigureAwait(false);
        return result.IsNull ? null : (long?)result;
    }

    /// <inheritdoc />
    public async Task<bool> IsCurrentAsync(string scope, string owner, long fencingToken, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var script = "return redis.call('HGET', KEYS[1], 'owner') == ARGV[1] and tonumber(redis.call('HGET', KEYS[1], 'fence') or '-1') == tonumber(ARGV[2]) and tonumber(redis.call('HGET', KEYS[1], 'until') or '0') > tonumber(ARGV[3]) and 1 or 0";
        return (long)await _database.ScriptEvaluateAsync(script, new RedisKey[] { LeaseKey(scope) },
            new RedisValue[] { owner, fencingToken, nowUtc.ToUnixTimeMilliseconds() }).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string scope, string owner, long fencingToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var script = "if redis.call('HGET', KEYS[1], 'owner') == ARGV[1] and tonumber(redis.call('HGET', KEYS[1], 'fence') or '-1') == tonumber(ARGV[2]) then return redis.call('DEL', KEYS[1]) end return 0";
        await _database.ScriptEvaluateAsync(script, new RedisKey[] { LeaseKey(scope) },
            new RedisValue[] { owner, fencingToken }).ConfigureAwait(false);
    }

    private static RedisKey LeaseKey(string scope) => "lycia:lease:" + scope;
    private static RedisKey FenceKey(string scope) => "lycia:fence:" + scope;
}
