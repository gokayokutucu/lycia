﻿using Newtonsoft.Json;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Helpers;
using StackExchange.Redis;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Helpers;
using Lycia.Saga.Enums;
using Lycia.Saga.Exceptions;

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
        var applicationId = ApplicationId();
        var messageTypeName = GetMessageTypeName(stepType);
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
                payload);
            
            var result = SagaStepHelper.ValidateSagaStepTransition(messageId, parentMessageId, status, existingSteps.Values, stepKey, metadata, existingMeta);
            
            var updated = false;
            var shouldBreak = false;
            switch (result.ValidationResult)
            {
                case SagaStepValidationResult.ValidTransition:
                    var newMetaJson = JsonHelper.SerializeSafe(metadata);
                    // 2. Try atomic update (CAS: Compare-And-Set)
                    updated = await TryAtomicUpdate(redisStepLogKey, stepKey, existingMetaJson, newMetaJson);
                    if (updated)  await SetExpiryAsync(redisStepLogKey);
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
            await Task.Delay(5 * attempt);
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
        {
            return null;
        }

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
    
    private string? ApplicationId()
    => !string.IsNullOrWhiteSpace(_options.ApplicationId)
            ? _options.ApplicationId
            : throw new InvalidOperationException("ApplicationId is required");

    private static string GetMessageTypeName(Type stepType)
    {
        return stepType.AssemblyQualifiedName ?? throw new InvalidOperationException(
            $"Step type {stepType.FullName} does not have an AssemblyQualifiedName");
    }
}