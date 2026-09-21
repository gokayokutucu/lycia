// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using StackExchange.Redis;

namespace Lycia.Persistence.Redis;

/// <summary>Redis materialization used only as the rebuildable operational side of Split Store.</summary>
public sealed class RedisOperationalSagaProjectionStore(IDatabase database) : IOperationalSagaProjectionStore
{
    private static readonly string ApplyScript = @"
local current = redis.call('get', KEYS[1])
local currentVersion = 0
if current then
  local ok, decoded = pcall(cjson.decode, current)
  if not ok or not decoded or not decoded.Version then return {-2, 0} end
  currentVersion = tonumber(decoded.Version)
end
local target = tonumber(ARGV[1])
if currentVersion > target then return {2, currentVersion} end
if currentVersion == target then return {1, currentVersion} end
redis.call('set', KEYS[1], ARGV[2])
return {0, target}";

    /// <inheritdoc />
    public async Task<ProjectionApplyOutcome> ApplyAsync(SagaProjectionIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = (RedisResult[])(await database.ScriptEvaluateAsync(ApplyScript,
            [Key(intent.SagaId)], [intent.TargetVersion, intent.Payload]).ConfigureAwait(false))!;
        return (long)result[0] switch
        {
            0 => ProjectionApplyOutcome.Applied,
            1 => ProjectionApplyOutcome.AlreadyApplied,
            2 => ProjectionApplyOutcome.Superseded,
            _ => ProjectionApplyOutcome.VersionConflict
        };
    }

    /// <inheritdoc />
    public async Task<long> GetVersionAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await database.StringGetAsync(Key(sagaId)).ConfigureAwait(false);
        if (!value.HasValue) return 0;
        try
        {
            var document = Newtonsoft.Json.Linq.JObject.Parse(value!);
            return document.Value<long?>(nameof(Lycia.Saga.Abstractions.Messaging.SagaData.Version)) ?? 0;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return -1;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await database.KeyDeleteAsync(Key(sagaId)).ConfigureAwait(false);
    }

    private static RedisKey Key(Guid sagaId) => $"saga:data:{sagaId}";
}
