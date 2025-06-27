using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Helpers;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace Lycia.Infrastructure.Stores;

/// <summary>
/// In-memory implementation of ISagaStore for testing or local development.
/// Not suitable for production environments.
/// </summary>
public class InMemorySagaStore(
    IEventBus eventBus,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator) : ISagaStore
{
    // Stores saga data per sagaId
    private readonly ConcurrentDictionary<Guid, SagaData> _sagaData = new();

    // Stores step logs per sagaId with composite key "stepTypeName_handlerTypeFullName"
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, SagaStepMetadata>> _stepLogs = new();

    /// <summary>
    /// Constructs the dictionary key used for storing step metadata when handlerType is unknown or not applicable.
    /// Uses only the step type name.
    /// </summary>
    private static string GetStepKey(Type stepType)
        => stepType.ToSagaStepName();

    public Task LogStepAsync(Guid sagaId, Type stepType, StepStatus status, Type handlerType, object? payload = null)
    {
        try
        {
            var stepDict = _stepLogs.GetOrAdd(sagaId, _ => new ConcurrentDictionary<string, SagaStepMetadata>());
            var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType);

            var messageTypeName = stepType.AssemblyQualifiedName ?? throw new InvalidOperationException(
                $"Step type {stepType.FullName} does not have an AssemblyQualifiedName");

            // State transition validation
            if (stepDict.TryGetValue(stepKey, out var existingMeta))
            {
                var previousStatus = existingMeta.Status;
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
                ApplicationId = "InMemory", // Replace with dynamic value if available
                MessagePayload = JsonConvert.SerializeObject(payload)
            };
            stepDict[stepKey] = metadata;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error logging step: {ex}");
            throw;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks if the step with specified stepType and handlerType is completed.
    /// Uses the composite key for lookup.
    /// </summary>
    public Task<bool> IsStepCompletedAsync(Guid sagaId, Type stepType, Type handlerType, Guid messageMessageId)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType);
            return Task.FromResult(
                steps.TryGetValue(stepKey, out var metadata) && metadata.Status == StepStatus.Completed
            );
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets the status of the step with specified stepType and handlerType.
    /// Uses the composite key for lookup.
    /// </summary>
    public Task<StepStatus> GetStepStatusAsync(Guid sagaId, Type stepType, Type handlerType)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType);
            if (steps.TryGetValue(stepKey, out var metadata))
            {
                return Task.FromResult(metadata.Status);
            }
        }

        return Task.FromResult(StepStatus.None);
    }

    /// <summary>
    /// Retrieves all saga handler steps for the given sagaId.
    /// Returns a dictionary keyed by (stepType, handlerType) tuple.
    /// </summary>
    public Task<IReadOnlyDictionary<(string stepType, string handlerType), SagaStepMetadata>>
        GetSagaHandlerStepsAsync(Guid sagaId)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            // Parse keys of the form "stepType_handlerType" into tuple keys
            var result = new Dictionary<(string stepType, string handlerType), SagaStepMetadata>();
            foreach (var kvp in steps)
            {
                var key = kvp.Key;
                var metadata = kvp.Value;

                var separatorIndex = key.IndexOf('_');
                if (separatorIndex > 0 && separatorIndex < key.Length - 1)
                {
                    var stepTypeName = key.Substring(0, separatorIndex);
                    var handlerTypeName = key.Substring(separatorIndex + 1);
                    result[(stepTypeName, handlerTypeName)] = metadata;
                }
                else
                {
                    // Fallback for keys without handler type part
                    result[(key, string.Empty)] = metadata;
                }
            }

            return Task
                .FromResult<IReadOnlyDictionary<(string stepType, string handlerType), SagaStepMetadata>>(result);
        }

        return Task.FromResult<IReadOnlyDictionary<(string stepType, string handlerType), SagaStepMetadata>>(
            new Dictionary<(string stepType, string handlerType), SagaStepMetadata>());
    }

    public Task<SagaData?> LoadSagaDataAsync(Guid sagaId)
    {
        _sagaData.TryGetValue(sagaId, out var data);
        return Task.FromResult(data);
    }

    public Task SaveSagaDataAsync(Guid sagaId, SagaData data)
    {
        _sagaData[sagaId] = data;
        return Task.CompletedTask;
    }

    public Task<ISagaContext<TStep, TSagaData>> LoadContextAsync<TStep, TSagaData>(Guid sagaId, TStep message, Type handlerType)
        where TSagaData : SagaData, new()
        where TStep : IMessage
    {
        if (!_sagaData.TryGetValue(sagaId, out var data))
        {
            data = new TSagaData();
            _sagaData[sagaId] = data;
        }

        ISagaContext<TStep, TSagaData> context = new SagaContext<TStep, TSagaData>(
            sagaId: sagaId,
            message: message,
            handlerType: handlerType,
            data: (TSagaData)data,
            eventBus: eventBus, // Use injected field
            sagaStore: this,
            sagaIdGenerator: sagaIdGenerator, // Use injected field
            compensationCoordinator: compensationCoordinator
        );

        return Task.FromResult(context);
    }
}