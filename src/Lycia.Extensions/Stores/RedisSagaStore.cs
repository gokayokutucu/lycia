// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Newtonsoft.Json;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Helpers;
using StackExchange.Redis;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Helpers;
using Lycia.Infrastructure.Helpers;
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
    : ISagaStore, Lycia.Saga.Abstractions.ISagaStoreHealthCheck
{
    private readonly SagaStoreOptions _options = options ?? new SagaStoreOptions();

    private static string SagaDataKey(Guid sagaId) => $"saga:data:{sagaId}";
    private static string StepLogKey(Guid sagaId) => $"saga:steps:{sagaId}";

    public Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, Exception? exception)
    {
        return LogStepAsync(sagaId, messageId, parentMessageId, stepType, status, handlerType, payload,
            new SagaStepFailureInfo("Exception occurred", exception?.GetType().Name, exception?.ToString()));
    }

    public async Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, SagaStepFailureInfo? failureInfo)
    {
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

    public async Task SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData? data)
        where TSagaData : SagaData
    {
        if (data is null) return;
        data.SagaId = sagaId;
        // Set the saga data in Redis, applying TTL/expiration if configured in options
        await redisDb.StringSetAsync(SagaDataKey(sagaId), JsonHelper.SerializeSafe(data), _options.StepLogTtl);
    }

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
            compensationCoordinator: sagaCompensationCoordinator
        );
        return context;
    }


    private string? ApplicationId()
        => !string.IsNullOrWhiteSpace(_options.ApplicationId)
            ? _options.ApplicationId
            : throw new InvalidOperationException("ApplicationId is required");

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
}