using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lycia.Saga; // For SagaData
using Lycia.Saga.Abstractions; // For ISagaStore
using Lycia.Messaging; // For IMessage
using Lycia.Messaging.Enums; // For StepStatus
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace OrderService.Infrastructure.Stores.Redis
{
    public class RedisSagaStore : ISagaStore
    {
        private readonly IDatabase _redisDatabase;
        private readonly string _instanceName; // Used as key prefix

        // Constructor for DI using IOptions
        public RedisSagaStore(IOptions<RedisSagaStoreOptions> options, IConnectionMultiplexer connectionMultiplexer)
        {
            if (options?.Value == null) throw new ArgumentNullException(nameof(options));
            if (connectionMultiplexer == null) throw new ArgumentNullException(nameof(connectionMultiplexer));

            _redisDatabase = connectionMultiplexer.GetDatabase();
            _instanceName = options.Value.InstanceName ?? "sagas"; // Default prefix
            if (!_instanceName.EndsWith(":"))
            {
                _instanceName += ":";
            }
        }

        private string GetSagaDataKey(Guid sagaId) => $"{_instanceName}data:{sagaId}";

        public async Task<SagaData> LoadSagaDataAsync(Guid sagaId)
        {
            var key = GetSagaDataKey(sagaId);
            RedisValue redisValue = await _redisDatabase.StringGetAsync(key);

            if (!redisValue.HasValue)
            {
                return null; // Saga not found or no data
            }

            try
            {
                // SagaData itself might be abstract or need a concrete type for deserialization
                // if it has derived types. Assuming SagaData can be directly deserialized.
                var sagaData = JsonSerializer.Deserialize<SagaData>(redisValue.ToString());
                return sagaData;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error deserializing SagaData for ID {sagaId}: {ex.Message}");
                return null; // Or throw, or return a new SagaData
            }
        }

        public async Task SaveSagaDataAsync(Guid sagaId, SagaData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var key = GetSagaDataKey(sagaId);
            var serializedSagaData = JsonSerializer.Serialize(data, typeof(SagaData)); // Serialize as base SagaData

            await _redisDatabase.StringSetAsync(key, serializedSagaData, TimeSpan.FromDays(7)); // Added expiry
        }

        // The following methods are part of ISagaStore but are not fully implemented yet.
        // Their implementation would depend on how step logging and context loading are designed for Redis.
        public Task LogStepAsync(Guid sagaId, Type stepType, StepStatus status, object payload = null)
        {
            Console.WriteLine($"LogStepAsync called for SagaId {sagaId}, Step {stepType.Name}, Status {status}. Not implemented in RedisSagaStore yet.");
            return Task.CompletedTask;
            // throw new NotImplementedException("Step logging in Redis is not fully implemented.");
        }

        public Task<bool> IsStepCompletedAsync(Guid sagaId, Type stepType)
        {
            Console.WriteLine($"IsStepCompletedAsync called for SagaId {sagaId}, Step {stepType.Name}. Not implemented in RedisSagaStore yet, returning false.");
            return Task.FromResult(false);
            // throw new NotImplementedException();
        }

        public Task<StepStatus> GetStepStatusAsync(Guid sagaId, Type stepType)
        {
             Console.WriteLine($"GetStepStatusAsync called for SagaId {sagaId}, Step {stepType.Name}. Not implemented in RedisSagaStore yet, returning None.");
            return Task.FromResult(StepStatus.None);
            // throw new NotImplementedException();
        }

        public Task<IReadOnlyDictionary<string, SagaStepMetadata>> GetSagaStepsAsync(Guid sagaId)
        {
            Console.WriteLine($"GetSagaStepsAsync called for SagaId {sagaId}. Not implemented in RedisSagaStore yet, returning empty.");
            return Task.FromResult<IReadOnlyDictionary<string, SagaStepMetadata>>(new Dictionary<string, SagaStepMetadata>());
            // throw new NotImplementedException();
        }

        // This method is crucial for Coordinated Sagas if they load context through ISagaStore.
        // The generic LoadAsync/SaveAsync/DeleteAsync from the original Lycia.Extensions.Stores.Redis
        // might be intended for a different ISagaStore pattern or for direct use.
        // The SagaDispatcher currently uses LoadSagaDataAsync and SaveSagaDataAsync.
        public async Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId)
            where TMessage : IMessage
            where TSagaData : SagaData, new()
        {
            // This implementation would require IEventBus, ISagaIdGenerator, etc. to be injected
            // into RedisSagaStore, or for SagaContext to be created differently.
            // For now, OrderService uses SagaDispatcher which calls LoadSagaDataAsync.
            // If a consumer or another part directly calls LoadContextAsync on ISagaStore, this needs full implementation.
            Console.WriteLine($"LoadContextAsync<TMessage, TSagaData> called for SagaId {sagaId}. Not fully implemented for RedisSagaStore, returning null.");
            // TSagaData loadedData = await LoadSagaDataAsync(sagaId) as TSagaData ?? new TSagaData();
            // return new SagaContext<TMessage, TSagaData>(sagaId, loadedData, /* IEventBus */, /* ISagaStore */, /* ISagaIdGenerator */);
            throw new NotImplementedException("LoadContextAsync<TMessage, TSagaData> is not fully implemented for RedisSagaStore in this context.");
        }
    }
}
