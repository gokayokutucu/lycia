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
    ISagaIdGenerator sagaIdGenerator
) : ISagaStore
{
    private static string SagaDataKey(Guid sagaId) => $"saga:data:{sagaId}";
    private static string StepLogKey(Guid sagaId) => $"saga:steps:{sagaId}";

    public async Task LogStepAsync(Guid sagaId, Type stepType, StepStatus status, Type handlerType, object? payload = null)
    {
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType);
        var routingKey = RoutingKeyHelper.GetRoutingKey(stepType);
        var applicationId = routingKey.Split('.')[0];
        var messageTypeName = stepType.AssemblyQualifiedName ?? stepType.ToSagaStepName();
        var redisStepLogKey = StepLogKey(sagaId);

        // State transition validation
        var existingMetaJson = await redisDb.HashGetAsync(redisStepLogKey, stepKey);
        if (existingMetaJson.HasValue)
        {
            var existingMeta = JsonConvert.DeserializeObject<SagaStepMetadata>(existingMetaJson!);
            var previousStatus = existingMeta?.Status ?? StepStatus.None;
            if (!SagaStepTransitionHelper.IsValidStepTransition(previousStatus, status))
            {
                var msg = $"Illegal StepStatus transition: {previousStatus} -> {status} for {stepKey}";
                Console.WriteLine(msg);
                throw new InvalidOperationException(msg);
            }
        }

        var metadata = new SagaStepMetadata
        {
            Status = status,
            MessageTypeName = messageTypeName,
            ApplicationId = applicationId, // TODO: Get from configuration or context if available
            MessagePayload = JsonConvert.SerializeObject(payload)
        };

        await redisDb.HashSetAsync(redisStepLogKey, stepKey, JsonConvert.SerializeObject(metadata));
    }

    public async Task<bool> IsStepCompletedAsync(Guid sagaId, Type stepType, Type handlerType)
    {
        var redisStepLogKey = StepLogKey(sagaId);
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType);

        var metaJson = await redisDb.HashGetAsync(redisStepLogKey, stepKey);
        if (!metaJson.HasValue)
            return false;

        var metadata = JsonConvert.DeserializeObject<SagaStepMetadata>(metaJson!);
        return metadata?.Status == StepStatus.Completed;
    }

    public async Task<StepStatus> GetStepStatusAsync(Guid sagaId, Type stepType, Type handlerType)
    {
        var redisStepLogKey = StepLogKey(sagaId);
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType);

        var metaJson = await redisDb.HashGetAsync(redisStepLogKey, stepKey);
        if (!metaJson.HasValue)
            return StepStatus.None;

        var metadata = JsonConvert.DeserializeObject<SagaStepMetadata>(metaJson!);
        return metadata?.Status ?? StepStatus.None;
    }

    public async Task<IReadOnlyDictionary<(string stepType, string handlerType), SagaStepMetadata>> GetSagaHandlerStepsAsync(Guid sagaId)
    {
        var redisStepLogKey = StepLogKey(sagaId);
        var entries = await redisDb.HashGetAllAsync(redisStepLogKey);
        var dict = new Dictionary<(string stepType, string handlerType), SagaStepMetadata>();

        foreach (var entry in entries)
        {
            var key = (string)entry.Name!;
            var separatorIndex = key.IndexOf('_');
            var metadata = JsonConvert.DeserializeObject<SagaStepMetadata>(entry.Value!)!;

            if (separatorIndex > 0 && separatorIndex < key.Length - 1)
            {
                var stepTypeName = key.Substring(0, separatorIndex);
                var handlerTypeName = key.Substring(separatorIndex + 1);
                dict[(stepTypeName, handlerTypeName)] = metadata;
            }
            else
            {
                dict[(key, string.Empty)] = metadata;
            }
        }

        return dict;
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
        await redisDb.StringSetAsync(SagaDataKey(sagaId), JsonConvert.SerializeObject(data));
    }

    public async Task<ISagaContext<TStep, TSagaData>> LoadContextAsync<TStep, TSagaData>(Guid sagaId, Type handlerType)
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
            handlerType: handlerType,
            data: data,
            eventBus: eventBus,
            sagaStore: this,
            sagaIdGenerator: sagaIdGenerator
        );
        return context;
    }
}