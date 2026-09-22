// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Newtonsoft.Json;
using Lycia.Common.Enums;
using Lycia.Common.Helpers;
using Lycia.Common.SagaSteps;
using StackExchange.Redis;
using Lycia.Extensions;
using Lycia.Extensions.Configurations;
using Lycia.Helpers;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Contexts;
using Lycia.Saga.Exceptions;
using Lycia.Saga.Helpers;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Abstractions.Outbox;

namespace Lycia.Persistence.Redis;

/// <summary>
/// Redis-backed implementation of ISagaStore for distributed environments.
/// </summary>
public class RedisSagaStore(
    IDatabase redisDb,
    IEventBus eventBus,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator sagaCompensationCoordinator,
    SagaStoreOptions? options,
    IMessageScheduler? messageScheduler = null,
    IOutgoingMessagePipeline? outgoingMessagePipeline = null)
    : ISagaStore, ISagaStoreHealthCheck, IVersionedSagaStore
{
    private readonly SagaStoreOptions _options = options ?? new SagaStoreOptions();

    private static string SagaDataKey(Guid sagaId) => $"saga:data:{sagaId}";
    private static string StepLogKey(Guid sagaId) => $"saga:steps:{sagaId}";
    private const string CompensationPendingKey = "saga:compensation:pending";
    private static string CompensationEdgeKey(Guid sagaId, Guid childMessageId) => $"saga:compensation:{sagaId}:{childMessageId}";
    private static string CompensationMember(Guid sagaId, Guid childMessageId) => $"{sagaId}|{childMessageId}";

    // The Lua scripts above write/compare CompensationPropagationStatus as a string (e.g. 'Completed'),
    // matching cjson's Lua-table representation. Newtonsoft's default enum serialization writes an int, so
    // any C#-side rewrite of an edge (MarkCompensationPropagationCompletedAsync/FailedAsync) must use this
    // converter too, or a later Lua read of that edge silently fails every string status comparison.
    private static readonly JsonSerializerSettings CompensationIntentJsonSettings = new()
    {
        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
    };

    // Idempotently creates the edge as Pending (immediately due) if absent, then attempts to claim it in
    // the same atomic script: mutual exclusion is entirely in the claim half, so two callers racing the
    // same edge can both reach the create half safely but at most one receives status "claimed".
    private const string EnsureAndClaimCompensationScript = @"
local edgeKey = KEYS[1]
local pendingKey = KEYS[2]
local sagaId = ARGV[1]
local childId = ARGV[2]
local parentId = ARGV[3]
local owner = ARGV[4]
local leaseMs = tonumber(ARGV[5])
local maxAttempts = tonumber(ARGV[6])
local nowMs = tonumber(ARGV[7])
local nowIso = ARGV[8]
local member = ARGV[9]

local json = redis.call('get', edgeKey)
local edge
if json then
  edge = cjson.decode(json)
else
  edge = { SagaId = sagaId, ChildMessageId = childId, ParentMessageId = parentId, Status = 'Pending',
           AttemptCount = 0, Owner = cjson.null, CreatedAtUtc = nowIso, UpdatedAtUtc = nowIso, FailureInfo = cjson.null }
  redis.call('set', edgeKey, cjson.encode(edge))
  redis.call('zadd', pendingKey, nowMs, member)
end

if edge.Status == 'Completed' then
  return {'AlreadyCompleted', cjson.encode(edge)}
end
if edge.Status == 'Failed' then
  return {'AttemptsExhausted', cjson.encode(edge)}
end
if edge.Status == 'Claimed' then
  -- Staleness is judged by the pending set's own due-score, which every successful claim (re)schedules
  -- to now+lease: a live claim's member score is still in the future, a stale one's is not (or the
  -- member is absent, e.g. already picked up by a batch claim pass).
  local dueScore = redis.call('zscore', pendingKey, member)
  if dueScore and tonumber(dueScore) > nowMs then
    return {'ClaimedByAnother', cjson.encode(edge)}
  end
end
if tonumber(edge.AttemptCount) >= maxAttempts then
  edge.Status = 'Failed'
  edge.UpdatedAtUtc = nowIso
  if edge.FailureInfo == cjson.null then
    edge.FailureInfo = { Reason = 'Compensation propagation attempts exhausted', ExceptionType = cjson.null, ExceptionDetail = cjson.null }
  end
  redis.call('set', edgeKey, cjson.encode(edge))
  redis.call('zrem', pendingKey, member)
  return {'AttemptsExhausted', cjson.encode(edge)}
end

edge.Status = 'Claimed'
edge.Owner = owner
edge.AttemptCount = tonumber(edge.AttemptCount) + 1
edge.UpdatedAtUtc = nowIso
redis.call('set', edgeKey, cjson.encode(edge))
redis.call('zadd', pendingKey, nowMs + leaseMs, member)
return {'Claimed', cjson.encode(edge)}";

    // Claims up to maxCount due edges (Pending, or Claimed whose lease/schedule has passed) exactly like
    // ClaimPendingBatchAsync does for the Outbox: pop the oldest-due members, re-check eligibility against
    // the edge's own record (never trust the queue alone), and reschedule the ones claimed to now+lease.
    private const string ClaimDueCompensationScript = @"
local pendingKey = KEYS[1]
local maxCount = tonumber(ARGV[1])
local owner = ARGV[2]
local leaseMs = tonumber(ARGV[3])
local maxAttempts = tonumber(ARGV[4])
local nowMs = tonumber(ARGV[5])
local nowIso = ARGV[6]
local edgePrefix = ARGV[7]
local results = {}
if maxCount <= 0 then return results end
local members = redis.call('zrangebyscore', pendingKey, '-inf', nowMs, 'LIMIT', 0, maxCount)
for i = 1, #members do
  local member = members[i]
  -- member is ""sagaId|childMessageId"" (CompensationMember); the real edge key is
  -- ""<edgePrefix>sagaId:childMessageId"" (CompensationEdgeKey) - translate the separator, don't
  -- concatenate the member as-is, or this never finds the edge and silently drops every entry.
  local edgeKey = edgePrefix .. string.gsub(member, '|', ':')
  local json = redis.call('get', edgeKey)
  if json then
    local edge = cjson.decode(json)
    if edge.Status == 'Pending' or edge.Status == 'Claimed' then
      if tonumber(edge.AttemptCount) >= maxAttempts then
        edge.Status = 'Failed'
        edge.UpdatedAtUtc = nowIso
        if edge.FailureInfo == cjson.null then
          edge.FailureInfo = { Reason = 'Compensation propagation attempts exhausted', ExceptionType = cjson.null, ExceptionDetail = cjson.null }
        end
        redis.call('set', edgeKey, cjson.encode(edge))
        redis.call('zrem', pendingKey, member)
      else
        edge.Status = 'Claimed'
        edge.Owner = owner
        edge.AttemptCount = tonumber(edge.AttemptCount) + 1
        edge.UpdatedAtUtc = nowIso
        redis.call('set', edgeKey, cjson.encode(edge))
        redis.call('zadd', pendingKey, nowMs + leaseMs, member)
        table.insert(results, cjson.encode(edge))
      end
    end
  else
    redis.call('zrem', pendingKey, member)
  end
end
return results";

    // Atomically saves the saga-data blob only if the currently stored Version equals expectedVersion.
    // Returns the new version on success, or -1 (with the actual stored version) on mismatch.
    private static readonly string AtomicSaveWithVersionScript = @"
local current = redis.call('get', KEYS[1])
local currentVersion = 0
if current then
  local ok, decoded = pcall(cjson.decode, current)
  if ok and decoded and decoded.Version then
    currentVersion = decoded.Version
  end
end
if currentVersion ~= tonumber(ARGV[1]) then
  return {-1, currentVersion}
end
redis.call('set', KEYS[1], ARGV[2])
if ARGV[3] ~= '' then
  redis.call('expire', KEYS[1], ARGV[3])
end
return {1, tonumber(ARGV[1]) + 1}";

    /// <inheritdoc />
    public Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, Exception? exception, CancellationToken cancellationToken = default)
    {
        return LogStepAsync(sagaId, messageId, parentMessageId, stepType, status, handlerType, payload,
            new SagaStepFailureInfo("Exception occurred", exception?.GetType().Name, exception?.ToString()),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, SagaStepFailureInfo? failureInfo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);
        var applicationId = ApplicationId();
        var messageTypeName = SagaStoreLogicHelper.GetMessageTypeName(stepType);
        var redisStepLogKey = StepLogKey(sagaId);

        var existingSteps = await GetSagaHandlerStepsAsync(sagaId);

        // Atomic update retry config
        var attempt = 0;
        while (true)
        {
            attempt++;
            // 1. Read current value (if exists)
            var existingMetaJson = await redisDb.HashGetAsync(redisStepLogKey, stepKey);

            var existingMeta = existingMetaJson.HasValue
                ? JsonConvert.DeserializeObject<SagaStepMetadata>(existingMetaJson!)
                : null;

            var metadata = SagaStepMetadata.Build(status, messageId, parentMessageId, messageTypeName, applicationId,
                payload, failureInfo);

            var result = SagaStepHelper.ValidateSagaStepTransition(messageId, parentMessageId, status,
                existingSteps.Values, stepKey, metadata, existingMeta);

            var updated = false;
            var shouldBreak = false;
            switch (result.ValidationResult)
            {
                case SagaStepValidationResult.ValidTransition:
                    var newMetaJson = JsonHelper.SerializeSafe(metadata);
                    // 2. Try atomic update (CAS: Compare-And-Set)
                    updated = await TryAtomicUpdate(redisStepLogKey, stepKey, existingMetaJson, newMetaJson);
                    if (updated) await SetExpiryAsync(redisStepLogKey);
                    break;
                case SagaStepValidationResult.Idempotent:
                    // Silently ignore idempotent updates
                    shouldBreak = true;
                    break;
                case SagaStepValidationResult.DuplicateWithDifferentPayload:
                    throw new SagaStepIdempotencyException(result.Message);
                case SagaStepValidationResult.InvalidTransition:
                    throw new SagaStepTransitionException(result.Message);
                case SagaStepValidationResult.CircularChain:
                    throw new SagaStepCircularChainException(result.Message);
                default:
                    throw new InvalidOperationException("Unexpected validation result: " + result.ValidationResult);
            }

            if (shouldBreak || updated) break; // Successfully updated or idempotent

            // CAS failed: another process/thread modified! Try again up to maxRetry
            if (attempt >= _options.LogMaxRetryCount)
            {
                throw new InvalidOperationException(
                    $"Concurrent update conflict on saga step after {_options.LogMaxRetryCount} attempts: {stepKey}");
            }

            // Optionally: add Task.Delay(10 * attempt) for backoff
            await Task.Delay(5 * attempt, cancellationToken);
        }
    }


    /// <summary>
    /// Attempts an atomic update on the saga step log field.
    /// </summary>
    private async Task<bool> TryAtomicUpdate(
        string redisStepLogKey,
        string stepKey,
        RedisValue existingMetaJson,
        string newMetaJson)
    {
        if (existingMetaJson.HasValue)
        {
            // Only update if old value matches (atomic)
            return await RedisHelper.HashSetFieldIfEqualAsync(
                redisDb,
                redisStepLogKey,
                field: stepKey,
                expectedOldValue: existingMetaJson.ToString(),
                newValue: newMetaJson);
        }

        // Create new if not exists (atomic)
        return await RedisHelper.HashSetFieldIfEqualAsync(
            redisDb,
            redisStepLogKey,
            field: stepKey,
            expectedOldValue: "",
            newValue: newMetaJson);
    }

    /// <summary>
    /// Sets expiry/TTL for the saga step log key.
    /// </summary>
    private async Task SetExpiryAsync(string redisStepLogKey)
    {
        await redisDb.KeyExpireAsync(redisStepLogKey, _options.StepLogTtl ?? TimeSpan.FromHours(1));
    }

    /// <inheritdoc />
    public async Task<bool> IsStepCompletedAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType)
    {
        var redisStepLogKey = StepLogKey(sagaId);
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);

        var metaJson = await redisDb.HashGetAsync(redisStepLogKey, stepKey);
        if (!metaJson.HasValue)
            return false;

        var metadata = JsonConvert.DeserializeObject<SagaStepMetadata>(metaJson!);
        return metadata?.Status == StepStatus.Completed;
    }

    /// <inheritdoc />
    public async Task<StepStatus> GetStepStatusAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType)
    {
        var redisStepLogKey = StepLogKey(sagaId);
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);

        var metaJson = await redisDb.HashGetAsync(redisStepLogKey, stepKey);
        if (!metaJson.HasValue)
            return StepStatus.None;

        var metadata = JsonConvert.DeserializeObject<SagaStepMetadata>(metaJson!);
        return metadata?.Status ?? StepStatus.None;
    }

    /// <inheritdoc />
    public async Task<KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>?>
        GetSagaHandlerStepAsync(Guid sagaId, Guid messageId)
    {
        var redisStepLogKey = StepLogKey(sagaId);
        var entries = await redisDb.HashGetAllAsync(redisStepLogKey);

        foreach (var entry in entries)
        {
            var key = (string)entry.Name!;
            var (stepTypeName, handlerTypeName, msgId) = SagaStoreLogicHelper.ParseStepKey(key);

            if (msgId != messageId.ToString()) continue;

            var metadata = JsonConvert.DeserializeObject<SagaStepMetadata>(entry.Value!)!;
            return new KeyValuePair<(string, string, string), SagaStepMetadata>(
                (stepTypeName, handlerTypeName, msgId), metadata
            );
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>>
        GetSagaHandlerStepsAsync(Guid sagaId)
    {
        var redisStepLogKey = StepLogKey(sagaId);
        var entries = await redisDb.HashGetAllAsync(redisStepLogKey);
        var result = new Dictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>();

        foreach (var entry in entries)
        {
            var key = (string)entry.Name!;
            var metadata = JsonConvert.DeserializeObject<SagaStepMetadata>(entry.Value!)!;

            var (stepTypeName, handlerTypeName, messageId) = SagaStoreLogicHelper.ParseStepKey(key);
            result[(stepTypeName, handlerTypeName, messageId)] = metadata;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Type stepType)
    {
        var redisStepLogKey = StepLogKey(sagaId);
        var entries = await redisDb.HashGetAllAsync(redisStepLogKey);

        foreach (var entry in entries)
        {
            try
            {
                var key = (string)entry.Name!;
                var (stepTypeName, _, _) = SagaStoreLogicHelper.ParseStepKey(key);
                if (stepTypeName != stepType.GetSimplifiedQualifiedName()) continue;

                var meta = JsonConvert.DeserializeObject<SagaStepMetadata>(entry.Value!)!;
                var payloadType = Type.GetType(meta.MessageTypeName);
                if (payloadType == null) continue;

                if (JsonConvert.DeserializeObject(meta.MessagePayload, payloadType) is IMessage messageObject) return messageObject;
            }
            catch
            {
                // Ignore malformed keys
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Guid messageId)
    {
        var redisStepLogKey = StepLogKey(sagaId);
        var entries = await redisDb.HashGetAllAsync(redisStepLogKey);

        foreach (var entry in entries)
        {
            try
            {
                var key = (string)entry.Name!;
                var (_, _, msgId) = SagaStoreLogicHelper.ParseStepKey(key);
                if (msgId != messageId.ToString()) continue;

                var meta = JsonConvert.DeserializeObject<SagaStepMetadata>(entry.Value!)!;
                var payloadType = Type.GetType(meta.MessageTypeName);
                if (payloadType == null) continue;

                return JsonConvert.DeserializeObject(meta.MessagePayload, payloadType) as IMessage;
            }
            catch
            {
                // Ignore malformed keys
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<TSagaData> LoadSagaDataAsync<TSagaData>(Guid sagaId)
        where TSagaData : SagaData, new()
    {
        var dataJson = await redisDb.StringGetAsync(SagaDataKey(sagaId));
        if (dataJson.HasValue) return JsonConvert.DeserializeObject<TSagaData>(dataJson!)!;
        // Return a new instance if nothing is found
        var emptyData = new TSagaData();
        await SaveSagaDataAsync(sagaId, emptyData);
        return emptyData;
    }

    /// <inheritdoc />
    public async Task SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData? data,
        CancellationToken cancellationToken = default)
        where TSagaData : SagaData
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (data is null) return;
        data.SagaId = sagaId;
        // Set the saga data in Redis, applying TTL/expiration if configured in options. StackExchange.Redis
        // does not accept a CancellationToken on this call; the guard above still stops an already-cancelled
        // operation from starting.
        await redisDb.StringSetAsync(SagaDataKey(sagaId), JsonHelper.SerializeSafe(data), _options.StepLogTtl);
    }

    /// <inheritdoc />
    public async Task<long> SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData data, long expectedVersion,
        CancellationToken cancellationToken = default)
        where TSagaData : SagaData
    {
        cancellationToken.ThrowIfCancellationRequested();
        data.SagaId = sagaId;
        data.Version = expectedVersion + 1;
        var newDataJson = JsonHelper.SerializeSafe(data);
        var ttlSeconds = _options.StepLogTtl.HasValue
            ? ((long)_options.StepLogTtl.Value.TotalSeconds).ToString()
            : "";

        var result = (RedisResult[])(await redisDb.ScriptEvaluateAsync(
            AtomicSaveWithVersionScript,
            [SagaDataKey(sagaId)],
            [expectedVersion, newDataJson, ttlSeconds]))!;

        var success = (long)result[0] == 1;
        var versionOrActual = (long)result[1];

        if (!success)
            throw new SagaConcurrencyException(sagaId, expectedVersion, versionOrActual);

        return versionOrActual;
    }

    /// <inheritdoc />
    public async Task<(TSagaData Data, long Version)> LoadSagaDataWithVersionAsync<TSagaData>(Guid sagaId,
        CancellationToken cancellationToken = default)
        where TSagaData : SagaData, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dataJson = await redisDb.StringGetAsync(SagaDataKey(sagaId));
        if (!dataJson.HasValue) return (new TSagaData(), 0);

        var data = JsonConvert.DeserializeObject<TSagaData>(dataJson!)!;
        return (data, data.Version);
    }

    /// <inheritdoc />
    public async Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId,
        TMessage message, Type handlerType)
        where TMessage : IMessage
        where TSagaData : SagaData
    {
        TSagaData? data = null;
        var dataJson = await redisDb.StringGetAsync(SagaDataKey(sagaId));
        if (dataJson.HasValue)
        {
            data = JsonConvert.DeserializeObject<TSagaData>(dataJson!);
        }

        if (data == null)
            throw new InvalidOperationException(
                $"SagaData instance could not be loaded or created. " +
                $"Please ensure a non-null state is available for saga: {sagaId}");


        ISagaContext<TMessage, TSagaData> context = new SagaContext<TMessage, TSagaData>(
            sagaId: sagaId,
            currentStep: message,
            handlerTypeOfCurrentStep: handlerType,
            data: data,
            eventBus: eventBus,
            sagaStore: this,
            sagaIdGenerator: sagaIdGenerator,
            compensationCoordinator: sagaCompensationCoordinator,
            messageScheduler: messageScheduler,
            outgoingMessagePipeline: outgoingMessagePipeline
        );
        return context;
    }


    private string? ApplicationId()
        => !string.IsNullOrWhiteSpace(_options.ApplicationId)
            ? _options.ApplicationId
            : throw new InvalidOperationException("ApplicationId is required");

    /// <inheritdoc />
    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await redisDb.PingAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<CompensationPropagationClaim> EnsureAndClaimCompensationPropagationAsync(Guid sagaId,
        Guid childMessageId, Guid parentMessageId, string owner, TimeSpan leaseDuration, int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTime.UtcNow;
        var result = (RedisResult[])(await redisDb.ScriptEvaluateAsync(
            EnsureAndClaimCompensationScript,
            [CompensationEdgeKey(sagaId, childMessageId), CompensationPendingKey],
            [sagaId.ToString(), childMessageId.ToString(), parentMessageId.ToString(), owner,
                (long)leaseDuration.TotalMilliseconds, maxAttempts, new DateTimeOffset(now).ToUnixTimeMilliseconds(),
                now.ToString("O"), CompensationMember(sagaId, childMessageId)]))!;

        return ToClaim(result);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompensationPropagationIntent>> ClaimDueCompensationPropagationsAsync(
        int maxCount, string owner, TimeSpan leaseDuration, TimeSpan recoveryTimeout, int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTime.UtcNow;
        var result = (RedisResult[])(await redisDb.ScriptEvaluateAsync(
            ClaimDueCompensationScript,
            [CompensationPendingKey],
            [maxCount, owner, (long)leaseDuration.TotalMilliseconds, maxAttempts,
                new DateTimeOffset(now).ToUnixTimeMilliseconds(), now.ToString("O"), "saga:compensation:"]))!;

        return result.Select(r => JsonConvert.DeserializeObject<CompensationPropagationIntent>((string)r!)!).ToList();
    }

    /// <inheritdoc />
    public async Task MarkCompensationPropagationCompletedAsync(Guid sagaId, Guid childMessageId,
        CancellationToken cancellationToken = default)
    {
        var key = CompensationEdgeKey(sagaId, childMessageId);
        var json = await redisDb.StringGetAsync(key);
        if (!json.HasValue) return;

        var intent = JsonConvert.DeserializeObject<CompensationPropagationIntent>(json!)!;
        intent.Status = CompensationPropagationStatus.Completed;
        intent.UpdatedAtUtc = DateTime.UtcNow;
        await redisDb.StringSetAsync(key, JsonConvert.SerializeObject(intent, CompensationIntentJsonSettings));
        await redisDb.SortedSetRemoveAsync(CompensationPendingKey, CompensationMember(sagaId, childMessageId));
    }

    /// <inheritdoc />
    public async Task MarkCompensationPropagationFailedAsync(Guid sagaId, Guid childMessageId,
        SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default)
    {
        var key = CompensationEdgeKey(sagaId, childMessageId);
        var json = await redisDb.StringGetAsync(key);
        if (!json.HasValue) return;

        var intent = JsonConvert.DeserializeObject<CompensationPropagationIntent>(json!)!;
        intent.Status = CompensationPropagationStatus.Failed;
        intent.FailureInfo = failureInfo;
        intent.UpdatedAtUtc = DateTime.UtcNow;
        await redisDb.StringSetAsync(key, JsonConvert.SerializeObject(intent, CompensationIntentJsonSettings));
        await redisDb.SortedSetRemoveAsync(CompensationPendingKey, CompensationMember(sagaId, childMessageId));
    }

    /// <inheritdoc />
    public async Task<CompensationPropagationIntent?> GetCompensationPropagationIntentAsync(Guid sagaId,
        Guid childMessageId, CancellationToken cancellationToken = default)
    {
        var json = await redisDb.StringGetAsync(CompensationEdgeKey(sagaId, childMessageId));
        return json.HasValue ? JsonConvert.DeserializeObject<CompensationPropagationIntent>(json!) : null;
    }

    private static CompensationPropagationClaim ToClaim(RedisResult[] result)
    {
        var outcome = (CompensationPropagationClaimOutcome)Enum.Parse(typeof(CompensationPropagationClaimOutcome), (string)result[0]!);
        var intent = JsonConvert.DeserializeObject<CompensationPropagationIntent>((string)result[1]!);
        return new CompensationPropagationClaim(outcome, intent);
    }
}
