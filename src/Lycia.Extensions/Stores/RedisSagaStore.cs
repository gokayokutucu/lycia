using Lycia.Infrastructure.Helpers;
using Newtonsoft.Json;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Helpers;
using StackExchange.Redis;

namespace Lycia.Extensions.Stores;

/// <summary>
/// Redis-backed implementation of ISagaStore for distributed environments.
/// </summary>
public class RedisSagaStore(
    IDatabase redisDb,
    IEventBus eventBus,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator sagaCompensationCoordinator,
    TimeSpan? stepLogTtl = null // Optional TTL parameter, defaults to DefaultStepLogTtl if null
) : ISagaStore
{
    /// <summary>
    /// Default TTL for step log keys (1 hour).
    /// </summary>
    private static readonly TimeSpan DefaultStepLogTtl = TimeSpan.FromHours(1);

    private readonly TimeSpan _stepLogTtl = stepLogTtl ?? DefaultStepLogTtl;

    private static string SagaDataKey(Guid sagaId) => $"saga:data:{sagaId}";
    private static string StepLogKey(Guid sagaId) => $"saga:steps:{sagaId}";

    public async Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status, Type handlerType, object? payload = null)
    {
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);
        var routingKey = RoutingKeyHelper.GetRoutingKey(stepType);
        var applicationId = routingKey.Split('.')[0];
        var messageTypeName = GetMessageTypeName(stepType);
        var redisStepLogKey = StepLogKey(sagaId);

        // State transition validation
        var existingMetaJson = await redisDb.HashGetAsync(redisStepLogKey, stepKey);
        if (existingMetaJson.HasValue)
        {
            var existingMeta = JsonConvert.DeserializeObject<SagaStepMetadata>(existingMetaJson!);
            var previousStatus = existingMeta?.Status ?? StepStatus.None;
            if (!SagaStepHelper.IsValidStepTransition(previousStatus, status))
            {
                var msg = $"Illegal StepStatus transition: {previousStatus} -> {status} for {stepKey}";
                Console.WriteLine(msg);
                throw new InvalidOperationException(msg);
            }
        }

        var metadata = new SagaStepMetadata
        {
            Status = status,
            MessageId = messageId,
            ParentMessageId = parentMessageId,
            MessageTypeName = messageTypeName,
            ApplicationId = applicationId, // TODO: Get from configuration or context if available
            MessagePayload = JsonHelper.SerializeSafe(payload)
        };

        await redisDb.HashSetAsync(redisStepLogKey, stepKey, JsonHelper.SerializeSafe(metadata));
        // Ensure the step log key has an expiry (TTL) set to avoid memory bloat.
        // Expiry is updated on each log call (idempotent).
        await redisDb.KeyExpireAsync(redisStepLogKey, _stepLogTtl);
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

    public async Task<IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>> GetSagaHandlerStepsAsync(Guid sagaId)
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
                
                result[(stepTypeName, handlerTypeName, messageId )] = metadata;
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

    public async Task<ISagaContext<TStep, TSagaData>> LoadContextAsync<TStep, TSagaData>(Guid sagaId, TStep message, Type handlerType)
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
            compensationCoordinator:sagaCompensationCoordinator
        );
        return context;
    }
    
    private static string GetMessageTypeName(Type stepType)
    {
        return stepType.AssemblyQualifiedName ?? throw new InvalidOperationException(
            $"Step type {stepType.FullName} does not have an AssemblyQualifiedName");
    }
}