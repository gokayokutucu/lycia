using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.Options; // Required for IOptions
using StackExchange.Redis;

namespace Lycia.Extensions.Stores.Redis
{
    public class RedisSagaStore : ISagaStore
    {
        private readonly IDatabase _redisDatabase;
        private readonly string _keyPrefix;

        public RedisSagaStore(IDatabase redisDatabase, IOptions<RedisSagaStoreOptions> options)
        {
            _redisDatabase = redisDatabase ?? throw new ArgumentNullException(nameof(redisDatabase));
            _keyPrefix = options?.Value?.KeyPrefix ?? "sagas:";
            if (!_keyPrefix.EndsWith(":"))
            {
                _keyPrefix += ":";
            }
        }
        
        // Kept the simpler constructor for direct instantiation or if options are not used.
        // Consider removing if IOptions is always expected.
        public RedisSagaStore(IDatabase redisDatabase, string keyPrefix = "sagas:")
        {
            _redisDatabase = redisDatabase ?? throw new ArgumentNullException(nameof(redisDatabase));
            _keyPrefix = keyPrefix ?? "sagas:";
            if (!_keyPrefix.EndsWith(":"))
            {
                _keyPrefix += ":";
            }
        }

        public async Task<TSagaData> LoadAsync<TSagaData>(Guid sagaId, CancellationToken cancellationToken = default) where TSagaData : SagaData, new()
        {
            var key = _keyPrefix + sagaId.ToString();
            var redisValue = await _redisDatabase.StringGetAsync(key);

            if (!redisValue.HasValue)
            {
                return null; // Or return new TSagaData { Id = sagaId, IsNew = true }; depending on ISagaStore expectations
            }

            try
            {
                var sagaData = JsonSerializer.Deserialize<TSagaData>(redisValue.ToString());
                // sagaData.IsNew = false; // Assuming SagaData has an IsNew property managed by the store
                return sagaData;
            }
            catch (JsonException ex)
            {
                // Handle deserialization error, e.g., log it and return null or throw
                Console.WriteLine($"Error deserializing saga data for ID {sagaId}: {ex.Message}"); // Replace with proper logging
                return null;
            }
        }

        public async Task SaveAsync<TSagaData>(TSagaData sagaData, CancellationToken cancellationToken = default) where TSagaData : SagaData, new()
        {
            if (sagaData == null)
            {
                throw new ArgumentNullException(nameof(sagaData));
            }

            var key = _keyPrefix + sagaData.Extras["Id"].ToString();
            var serializedSagaData = JsonSerializer.Serialize(sagaData);

            // Consider adding expiry, e.g., TimeSpan.FromDays(7)
            await _redisDatabase.StringSetAsync(key, serializedSagaData); 
        }

        public async Task DeleteAsync<TSagaData>(Guid sagaId, CancellationToken cancellationToken = default) where TSagaData : SagaData, new()
        {
            var key = _keyPrefix + sagaId.ToString();
            await _redisDatabase.KeyDeleteAsync(key);
        }

        public async Task LogStepAsync(Guid sagaId, Type stepType, StepStatus status, object? payload = null)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> IsStepCompletedAsync(Guid sagaId, Type stepType)
        {
            throw new NotImplementedException();
        }

        public async Task<StepStatus> GetStepStatusAsync(Guid sagaId, Type stepType)
        {
            throw new NotImplementedException();
        }

        public async Task<IReadOnlyDictionary<string, SagaStepMetadata>> GetSagaStepsAsync(Guid sagaId)
        {
            throw new NotImplementedException();
        }

        public async Task<SagaData?> LoadSagaDataAsync(Guid sagaId)
        {
            throw new NotImplementedException();
        }

        public async Task SaveSagaDataAsync(Guid sagaId, SagaData data)
        {
            throw new NotImplementedException();
        }

        public async Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId)
            where TMessage : IMessage
            where TSagaData : SagaData, new()
        {
            throw new NotImplementedException();
        }
    }
}
