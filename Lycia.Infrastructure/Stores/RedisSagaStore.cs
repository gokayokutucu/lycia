using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lycia.Infrastructure.Redis;
using Lycia.Messaging.Enums; // For StepStatus
using Lycia.Saga; // For SagaData
using Lycia.Saga.Abstractions; // For ISagaStore
using Lycia.Saga.Extensions; // For ToSagaStepName
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Lycia.Messaging; // For IMessage
using Lycia.Saga.SagaContext; // Assuming SagaContext is here or similar path

namespace Lycia.Infrastructure.Stores
{
    // Helper class for serializing step log entries
    internal class StepLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string StepTypeName { get; set; } // Using stepType.ToSagaStepName()
        public StepStatus Status { get; set; }
        public string? SerializedPayload { get; set; }
        public string MessageAssemblyQualifiedName { get; set; } // To reconstruct Type for SagaStepMetadata

        public StepLogEntry(DateTime timestamp, string stepTypeName, StepStatus status, string? payload, string messageAssemblyQualifiedName)
        {
            Timestamp = timestamp;
            StepTypeName = stepTypeName;
            Status = status;
            SerializedPayload = payload;
            MessageAssemblyQualifiedName = messageAssemblyQualifiedName;
        }
    }

    public class RedisSagaStore : ISagaStore
    {
        private readonly IRedisConnectionFactory _redisConnectionFactory;
        private readonly ILogger<RedisSagaStore> _logger;
        private readonly IEventBus _eventBus; // Needed for LoadContextAsync
        private readonly ISagaIdGenerator _sagaIdGenerator; // Needed for LoadContextAsync

        private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            // Add any specific options if needed, e.g., for polymorphism if SagaData is extended.
            // For now, default options should suffice for SagaData.Extras and StepLogEntry.
            PropertyNameCaseInsensitive = true, // Good for resilience
        };

        public RedisSagaStore(
            IRedisConnectionFactory redisConnectionFactory,
            IEventBus eventBus, // Added for LoadContextAsync
            ISagaIdGenerator sagaIdGenerator, // Added for LoadContextAsync
            ILogger<RedisSagaStore>? logger)
        {
            _redisConnectionFactory = redisConnectionFactory ?? throw new ArgumentNullException(nameof(redisConnectionFactory));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus)); // Added
            _sagaIdGenerator = sagaIdGenerator ?? throw new ArgumentNullException(nameof(sagaIdGenerator)); // Added
            _logger = logger ?? NullLogger<RedisSagaStore>.Instance;
        }

        private string SagaDataKey(Guid sagaId) => $"saga:{sagaId}:data";
        private string SagaStepsLogKey(Guid sagaId) => $"saga:{sagaId}:steplog";
        private string SagaCompletedStepsKey(Guid sagaId) => $"saga:{sagaId}:completedsteps";

        public async Task<SagaData?> LoadSagaDataAsync(Guid sagaId)
        {
            _logger.LogDebug("Attempting to load SagaData for SagaId: {SagaId}", sagaId);
            try
            {
                var db = _redisConnectionFactory.GetDatabase();
                var key = SagaDataKey(sagaId);
                var serializedSagaData = await db.StringGetAsync(key);

                if (serializedSagaData.IsNullOrEmpty)
                {
                    _logger.LogDebug("No SagaData found in Redis for SagaId: {SagaId}", sagaId);
                    return null;
                }

                // Assuming SagaData is stored as a JSON string representing the concrete SagaData type.
                // This requires that the SagaData stored can be deserialized to the abstract SagaData
                // or a known concrete type. If using derived types of SagaData, type information
                // might be needed for correct deserialization if not handled by JsonSerializer settings.
                // For now, we assume the default (or a known concrete type if `LoadContextAsync` provides it).
                // This is a simplification. A robust solution would store type info or expect a concrete type.
                var sagaDataType = typeof(SagaData); // Placeholder; ideally, this would be the concrete type.
                                                     // In LoadContextAsync, TSagaData provides the concrete type.
                                                     // Here, we might need to store type information alongside the data.

                // Attempt to deserialize to a generic SagaData or a specific known type if applicable.
                // For this example, let's assume it's stored in a way that SagaData (base) can be deserialized.
                // Or, more practically, this method might be less used directly if LoadContextAsync is the primary entry point.
                // For now, we'll try to deserialize as the base SagaData. This is primarily for contexts where the concrete type isn't known.
                // LoadContextAsync handles specific TSagaData deserialization.
                var sagaData = JsonSerializer.Deserialize<SagaDataWrapperForDeserialization>(serializedSagaData!, _jsonSerializerOptions);
                
                _logger.LogInformation("Successfully loaded SagaData for SagaId: {SagaId} of type {SagaDataType}", sagaId, sagaData?.GetType().Name ?? "Unknown");
                return sagaData;
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON Deserialization error loading SagaData from Redis for SagaId: {SagaId}. Raw data: {RawData}", sagaId, serializedSagaData.ToString());
                throw; 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic error loading SagaData from Redis for SagaId: {SagaId}", sagaId);
                throw; // Or handle as per application's error strategy
            }
        }
        
        // Helper class because SagaData is abstract
        private class SagaDataWrapperForDeserialization : SagaData { }


        public async Task SaveSagaDataAsync(Guid sagaId, SagaData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            _logger.LogDebug("Attempting to save SagaData for SagaId: {SagaId}, Type: {SagaDataType}", sagaId, data.GetType().FullName);

            try
            {
                var db = _redisConnectionFactory.GetDatabase();
                var key = SagaDataKey(sagaId);

                // Serialize using the actual type of `data` for polymorphism
                var serializedSagaData = JsonSerializer.Serialize(data, data.GetType(), _jsonSerializerOptions);
                
                await db.StringSetAsync(key, serializedSagaData);
                _logger.LogInformation("Successfully saved SagaData to Redis for SagaId: {SagaId}, Type: {SagaDataType}", sagaId, data.GetType().FullName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving SagaData to Redis for SagaId: {SagaId}, Type: {SagaDataType}", sagaId, data.GetType().FullName);
                throw;
            }
        }
        
        public async Task LogStepAsync(Guid sagaId, Type stepType, StepStatus status, object? payload = null)
        {
            if (stepType == null) throw new ArgumentNullException(nameof(stepType));
            var stepName = stepType.ToSagaStepName();
            _logger.LogDebug("Attempting to log step {StepName} with status {Status} for SagaId: {SagaId}. Payload provided: {HasPayload}", 
                stepName, status, sagaId, payload != null);

            try
            {
                var db = _redisConnectionFactory.GetDatabase();
                var key = SagaStepsLogKey(sagaId);
                var messageAssemblyQualifiedName = stepType.AssemblyQualifiedName ?? stepType.FullName ?? stepName;

                string? serializedPayload = null;
                if (payload != null)
                {
                    serializedPayload = JsonSerializer.Serialize(payload, payload.GetType(), _jsonSerializerOptions);
                }

                var logEntry = new StepLogEntry(DateTime.UtcNow, stepName, status, serializedPayload, messageAssemblyQualifiedName);
                var serializedLogEntry = JsonSerializer.Serialize(logEntry, _jsonSerializerOptions);

                await db.ListRightPushAsync(key, serializedLogEntry);
                _logger.LogInformation("Logged step {StepName} with status {Status} for SagaId: {SagaId}. Payload size: {PayloadSize} bytes (approx)", 
                    stepName, status, sagaId, serializedPayload?.Length ?? 0);

                // If the step is completed, add it to the completed steps set for IsStepCompletedAsync optimization
                if (status == StepStatus.Completed)
                {
                    await db.SetAddAsync(SagaCompletedStepsKey(sagaId), stepName);
                    _logger.LogDebug("Added step {StepName} to completed set for SagaId: {SagaId}", stepName, sagaId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging step {StepName} with status {Status} to Redis for SagaId: {SagaId}", stepName, status, sagaId);
                throw;
            }
        }

        public async Task<bool> IsStepCompletedAsync(Guid sagaId, Type stepType)
        {
            if (stepType == null) throw new ArgumentNullException(nameof(stepType));
            var stepName = stepType.ToSagaStepName();
            _logger.LogDebug("Checking if step {StepName} is completed for SagaId: {SagaId}", stepName, sagaId);

            try
            {
                var db = _redisConnectionFactory.GetDatabase();
                var result = await db.SetContainsAsync(SagaCompletedStepsKey(sagaId), stepName);
                _logger.LogDebug("Step {StepName} for SagaId: {SagaId} is completed: {IsCompleted}", stepName, sagaId, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if step {StepName} is completed from Redis for SagaId: {SagaId}", stepName, sagaId);
                throw;
            }
        }

        public async Task<IReadOnlyDictionary<string, SagaStepMetadata>> GetSagaStepsAsync(Guid sagaId)
        {
            _logger.LogDebug("Attempting to get saga steps for SagaId: {SagaId}", sagaId);
            try
            {
                var db = _redisConnectionFactory.GetDatabase();
                var key = SagaStepsLogKey(sagaId);
                var logEntriesSerialized = await db.ListRangeAsync(key);

                var steps = new Dictionary<string, SagaStepMetadata>();
                if (logEntriesSerialized == null || !logEntriesSerialized.Any())
                {
                    _logger.LogInformation("No step logs found in Redis for SagaId: {SagaId}", sagaId);
                    return steps;
                }

                int deserializationErrors = 0;
                foreach (var entrySerialized in logEntriesSerialized)
                {
                    if (entrySerialized.IsNullOrEmpty) continue;

                    try
                    {
                        var logEntry = JsonSerializer.Deserialize<StepLogEntry>(entrySerialized!, _jsonSerializerOptions);
                        if (logEntry != null)
                        {
                            steps[logEntry.StepTypeName] = new SagaStepMetadata
                            {
                                Status = logEntry.Status,
                                MessageTypeName = logEntry.MessageAssemblyQualifiedName,
                                ApplicationId = "RedisStore", 
                                MessagePayload = logEntry.SerializedPayload
                            };
                        }
                        else
                        {
                            deserializationErrors++;
                            _logger.LogWarning("Failed to deserialize a step log entry for SagaId {SagaId}. Raw: {RawEntry}", sagaId, entrySerialized.ToString());
                        }
                    }
                    catch(JsonException jsonEx)
                    {
                        deserializationErrors++;
                        _logger.LogError(jsonEx, "JSON deserialization error for a step log entry for SagaId {SagaId}. Raw: {RawEntry}", sagaId, entrySerialized.ToString());
                    }
                }
                if (deserializationErrors > 0)
                {
                    _logger.LogWarning("Encountered {DeserializationErrorCount} errors while deserializing step logs for SagaId {SagaId}", deserializationErrors, sagaId);
                }
                _logger.LogInformation("Retrieved {StepCount} step logs from Redis for SagaId: {SagaId}", steps.Count, sagaId);
                return steps;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all saga steps from Redis for SagaId: {SagaId}", sagaId);
                throw;
            }
        }

        public async Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId)
            where TSagaData : SagaData, new()
            where TMessage : IMessage
        {
            _logger.LogDebug("Loading context for SagaId: {SagaId}, MessageType: {MessageType}, SagaDataType: {SagaDataType}", 
                sagaId, typeof(TMessage).Name, typeof(TSagaData).Name);

            SagaData? loadedData = null;
            try
            {
                var db = _redisConnectionFactory.GetDatabase();
                var key = SagaDataKey(sagaId);
                var serializedSagaData = await db.StringGetAsync(key);

                if (!serializedSagaData.IsNullOrEmpty)
                {
                    loadedData = JsonSerializer.Deserialize<TSagaData>(serializedSagaData!, _jsonSerializerOptions);
                    _logger.LogInformation("Successfully deserialized existing SagaData for SagaId: {SagaId} as type {SagaDataType}", sagaId, typeof(TSagaData).Name);
                }
                else
                {
                    _logger.LogInformation("No existing SagaData found for SagaId: {SagaId} of type {SagaDataType}. Will create new instance.", sagaId, typeof(TSagaData).Name);
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON Deserialization error loading SagaData for context for SagaId: {SagaId}. Raw data: {RawData}. Proceeding with new SagaData.", sagaId, serializedSagaData.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic error loading SagaData for context for SagaId: {SagaId}. Proceeding with new SagaData.", sagaId);
            }
            
            TSagaData sagaDataInstance;
            if (loadedData is TSagaData concreteData)
            {
                sagaDataInstance = concreteData;
            }
            else
            {
                if (loadedData != null) // Deserialized as something else, or base type
                {
                    _logger.LogWarning("Loaded SagaData for SagaId {SagaId} was not of expected type {ExpectedType} (was {ActualType}). Creating new TSagaData instance.",
                        sagaId, typeof(TSagaData).FullName, loadedData.GetType().FullName);
                }
                sagaDataInstance = new TSagaData(); 
                _logger.LogInformation("Created new SagaData instance of type {SagaDataType} for SagaId: {SagaId}", typeof(TSagaData).Name, sagaId);
            }

            var context = new SagaContext<TMessage, TSagaData>(
                sagaId: sagaId,
                data: sagaDataInstance,
                eventBus: _eventBus,
                sagaStore: this,
                sagaIdGenerator: _sagaIdGenerator
            );

            _logger.LogInformation("SagaContext created for SagaId: {SagaId}, MessageType: {MessageType}, SagaDataType: {SagaDataType}", sagaId, typeof(TMessage).Name, typeof(TSagaData).Name);
            return context;
        }
    }
}
