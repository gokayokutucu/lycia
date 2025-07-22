using Lycia.Infrastructure.Helpers;
using Newtonsoft.Json;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Helpers;
using StackExchange.Redis;
using Lycia.Extensions.Configurations;

namespace Lycia.Extensions.Stores;

/// <summary>
/// Redis-backed implementation of ISagaStore for distributed environments.
/// </summary>
public class RedisSagaStore(
    IDatabase redisDb,
    IEventBus eventBus,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator sagaCompensationCoordinator,
    SagaStoreOptions? options)
    : ISagaStore
{
    private readonly SagaStoreOptions _options = options ?? new SagaStoreOptions();

    private static string SagaDataKey(Guid sagaId) => $"saga:data:{sagaId}";
    private static string StepLogKey(Guid sagaId) => $"saga:steps:{sagaId}";

    public async Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload = null)
    {
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);
        var applicationId = !string.IsNullOrWhiteSpace(_options.ApplicationId)
            ? _options.ApplicationId
            : throw new InvalidOperationException("ApplicationId is required");
        var messageTypeName = GetMessageTypeName(stepType);
        var redisStepLogKey = StepLogKey(sagaId);

        // Atomic update retry config
        const int maxRetry = 5;
        int attempt = 0;

        while (true)
        {
            attempt++;

            // 1. Read current value (if exists)
            var existingMetaJson = await redisDb.HashGetAsync(redisStepLogKey, stepKey);

            var metadata = new SagaStepMetadata
            {
                Status = status,
                MessageId = messageId,
                ParentMessageId = parentMessageId,
                MessageTypeName = messageTypeName,
                ApplicationId = applicationId,
                MessagePayload = JsonHelper.SerializeSafe(payload)
            };

            if (existingMetaJson.HasValue)
            {
                var existingMeta = JsonConvert.DeserializeObject<SagaStepMetadata>(existingMetaJson!);
                var previousStatus = existingMeta?.Status ?? StepStatus.None;

                if (!SagaStepHelper.IsValidStepTransition(previousStatus, status))
                {
                    // Additional idempotency check: if status is the same
                    if (previousStatus == status && !existingMeta!.IsIdempotentWith(metadata))
                    {
                        var msg = $"Illegal idempotent update with differing metadata for {stepKey}";
                        throw new InvalidOperationException(msg);
                    }
                    else
                    {
                        var msg = $"Illegal StepStatus transition: {previousStatus} -> {status} for {stepKey}";
                        throw new InvalidOperationException(msg);
                    }
                }
            }

            var newMetaJson = JsonHelper.SerializeSafe(metadata);

            // 2. Try atomic update (CAS: Compare-And-Set)
            bool updated;
            if (existingMetaJson.HasValue)
            {
                // Only update if old value matches (atomic)
                updated = await HashSetFieldIfEqualAsync(
                    redisDb,
                    redisStepLogKey,
                    field: stepKey,
                    expectedOldValue: existingMetaJson.ToString(),
                    newValue: newMetaJson);
            }
            else
            {
                // Create new if not exists (atomic)
                updated = await HashSetFieldIfEqualAsync(
                    redisDb,
                    redisStepLogKey,
                    field: stepKey,
                    expectedOldValue: "",
                    newValue: newMetaJson);
            }

            if (updated)
            {
                // Set expiry/TTL
                await redisDb.KeyExpireAsync(redisStepLogKey, _options.StepLogTtl ?? TimeSpan.FromHours(1));
                break; // Success
            }
            else
            {
                // CAS failed: another process/thread modified! Try again up to maxRetry
                if (attempt >= maxRetry)
                {
                    throw new InvalidOperationException(
                        $"Concurrent update conflict on saga step after {maxRetry} attempts: {stepKey}");
                }

                // Optionally: add Task.Delay(10 * attempt) for backoff
                await Task.Delay(5 * attempt);
            }
        }
    }

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

    public async Task<IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>>
        GetSagaHandlerStepsAsync(Guid sagaId)
    {
        var redisStepLogKey = StepLogKey(sagaId);
        var entries = await redisDb.HashGetAllAsync(redisStepLogKey);
        var result = new Dictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>();

        foreach (var entry in entries)
        {
            var key = (string)entry.Name!;
            var parts = key.Split(':');
            var metadata = JsonConvert.DeserializeObject<SagaStepMetadata>(entry.Value!)!;

            if (parts.Length == 8 &&
                parts[0] == "step" &&
                parts[2] == "assembly" &&
                parts[4] == "handler" &&
                parts[6] == "message-id")
            {
                var stepTypeName = $"{parts[1]}, {parts[3]}"; // Combine step type and assembly
                var handlerTypeName = parts[5];
                var messageId = parts[7];

                result[(stepTypeName, handlerTypeName, messageId)] = metadata;
            }
            else
            {
                result[(key, string.Empty, Guid.Empty.ToString())] = metadata;
            }
        }

        return result;
    }

    public async Task<SagaData?> LoadSagaDataAsync(Guid sagaId)
    {
        var dataJson = await redisDb.StringGetAsync(SagaDataKey(sagaId));
        if (!dataJson.HasValue)
            return null;

        return JsonConvert.DeserializeObject<SagaData>(dataJson!);
    }

    public async Task SaveSagaDataAsync(Guid sagaId, SagaData data)
    {
        await redisDb.StringSetAsync(SagaDataKey(sagaId), JsonHelper.SerializeSafe(data));
    }

    public async Task<ISagaContext<TStep, TSagaData>> LoadContextAsync<TStep, TSagaData>(Guid sagaId, TStep message,
        Type handlerType)
        where TSagaData : SagaData, new()
        where TStep : IMessage
    {
        TSagaData data;
        var dataJson = await redisDb.StringGetAsync(SagaDataKey(sagaId));
        if (dataJson.HasValue)
        {
            data = JsonConvert.DeserializeObject<TSagaData>(dataJson!) ?? new TSagaData();
        }
        else
        {
            data = new TSagaData();
            await SaveSagaDataAsync(sagaId, data);
        }

        ISagaContext<TStep, TSagaData> context = new SagaContext<TStep, TSagaData>(
            sagaId: sagaId,
            currentContextMessage: message,
            handlerType: handlerType,
            data: data,
            eventBus: eventBus,
            sagaStore: this,
            sagaIdGenerator: sagaIdGenerator,
            compensationCoordinator: sagaCompensationCoordinator
        );
        return context;
    }

    private static readonly string AtomicHashSetIfEqualScript = @"
local current = redis.call('hget', KEYS[1], ARGV[1])
if (not current and ARGV[2] == '') or (current == ARGV[2]) then
  redis.call('hset', KEYS[1], ARGV[1], ARGV[3])
  return 1
else
  return 0
end";

    public async Task<bool> HashSetFieldIfEqualAsync(
        IDatabase redisDb,
        string hashKey,
        string field,
        string? expectedOldValue,
        string newValue)
    {
        // Empty string is special marker for non-existing
        var oldVal = expectedOldValue ?? "";
        var result = (int)(await redisDb.ScriptEvaluateAsync(
            AtomicHashSetIfEqualScript,
            [hashKey],
            [field, oldVal, newValue]
        ));
        return result == 1;
    }

    private static string GetMessageTypeName(Type stepType)
    {
        return stepType.AssemblyQualifiedName ?? throw new InvalidOperationException(
            $"Step type {stepType.FullName} does not have an AssemblyQualifiedName");
    }
}