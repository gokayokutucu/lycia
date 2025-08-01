using System.Collections.Concurrent;
using Lycia.Infrastructure.Helpers;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Enums;
using Lycia.Saga.Exceptions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Helpers;

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
    private readonly ConcurrentDictionary<Guid, object> _sagaData = new();

    // Stores step logs per sagaId with composite key "stepTypeName_handlerTypeFullName"
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, SagaStepMetadata>> _stepLogs = new();

    public Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload)
    {
        var stepDict = _stepLogs.GetOrAdd(sagaId, _ => new ConcurrentDictionary<string, SagaStepMetadata>());
        var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);

        stepDict.TryGetValue(stepKey, out var existingMeta);

        var messageTypeName = SagaStoreLogicHelper.GetMessageTypeName(stepType);

        var metadata = SagaStepMetadata.Build(
            status: status,
            messageId: messageId,
            parentMessageId: parentMessageId,
            messageTypeName: messageTypeName,
            applicationId: "InMemory",
            payload: payload);

        // State transition validation
        var result = SagaStepHelper.ValidateSagaStepTransition(messageId, parentMessageId, status, stepDict.Values,
            stepKey, metadata, existingMeta);

        switch (result.ValidationResult)
        {
            case SagaStepValidationResult.ValidTransition:
                stepDict[stepKey] = metadata;
                break;
            case SagaStepValidationResult.Idempotent:
                // Silently ignore idempotent updates
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

        return Task.CompletedTask;
    }


    /// <summary>
    /// Checks if the step with specified stepType and handlerType is completed.
    /// Uses the composite key for lookup.
    /// </summary>
    public Task<bool> IsStepCompletedAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);
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
    public Task<StepStatus> GetStepStatusAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            var stepKey = NamingHelper.GetStepNameWithHandler(stepType, handlerType, messageId);
            if (steps.TryGetValue(stepKey, out var metadata))
            {
                return Task.FromResult(metadata.Status);
            }
        }

        return Task.FromResult(StepStatus.None);
    }

    public Task<KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>?>
        GetSagaHandlerStepAsync(Guid sagaId, Guid messageId)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            foreach (var kvp in steps)
            {
                try
                {
                    var (stepTypeName, handlerTypeName, msgId) = SagaStoreLogicHelper.ParseStepKey(kvp.Key);
                    if (msgId == messageId.ToString())
                    {
                        return Task.FromResult<KeyValuePair<(string, string, string), SagaStepMetadata>?>(
                            new KeyValuePair<(string, string, string), SagaStepMetadata>(
                                (stepTypeName, handlerTypeName, msgId), kvp.Value));
                    }
                }
                catch
                {
                    // Ignore malformed keys
                }
            }
        }

        return Task.FromResult<KeyValuePair<(string, string, string), SagaStepMetadata>?>(null);
    }

    /// <summary>
    /// Retrieves all saga handler steps for the given sagaId.
    /// Returns a dictionary keyed by (stepType, handlerType) tuple.
    /// </summary>
    public Task<IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>>
        GetSagaHandlerStepsAsync(Guid sagaId)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            // Parse keys of the form "step:{stepType}:handler:{handlerType}:message:{messageId}"
            var result = new Dictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>();
            foreach (var kvp in steps)
            {
                var key = kvp.Key;
                var metadata = kvp.Value;

                try
                {
                    var (stepTypeName, handlerTypeName, messageId) = SagaStoreLogicHelper.ParseStepKey(key);
                    result[(stepTypeName, handlerTypeName, messageId)] = metadata;
                }
                catch
                {
                    // ignore malformed keys
                }
            }

            return Task
                .FromResult<IReadOnlyDictionary<(string stepType, string handlerType, string messageId),
                    SagaStepMetadata>>(result);
        }

        return Task
            .FromResult<IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>>(
                new Dictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>());
    }

    public Task<TSagaData?> LoadSagaDataAsync<TSagaData>(Guid sagaId)
        where TSagaData : class
    {
        _sagaData.TryGetValue(sagaId, out var data);
        return Task.FromResult(data as TSagaData);
    }

    public Task SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData? data)
    {
        if (data is null) return Task.CompletedTask;
        _sagaData[sagaId] = data;
        return Task.CompletedTask;
    }

    public Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId, TMessage message,
        Type handlerType)
        where TMessage : IMessage
        where TSagaData : new()
    {
        if (!_sagaData.TryGetValue(sagaId, out var data))
        {
            data = new TSagaData();
            _sagaData[sagaId] = data;
        }

        ISagaContext<TMessage, TSagaData> context = new SagaContext<TMessage, TSagaData>(
            sagaId: sagaId,
            currentStep: message,
            handlerTypeOfCurrentStep: handlerType,
            data: (TSagaData)data,
            eventBus: eventBus, // Use injected field
            sagaStore: this,
            sagaIdGenerator: sagaIdGenerator, // Use injected field
            compensationCoordinator: compensationCoordinator
        );

        return Task.FromResult(context);
    }
}