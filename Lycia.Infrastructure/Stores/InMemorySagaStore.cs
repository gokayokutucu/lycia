using System.Collections.Concurrent;
using System.Text.Json;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;

namespace Lycia.Infrastructure.Stores;

/// <summary>
/// In-memory implementation of ISagaStore for testing or local development.
/// Not suitable for production environments.
/// </summary>
public class InMemorySagaStore(IEventBus eventBus, ISagaIdGenerator sagaIdGenerator) : ISagaStore
{
    // Stores saga data per sagaId
    private readonly ConcurrentDictionary<Guid, SagaData> _sagaData = new();

    // Stores step logs per sagaId
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, SagaStepMetadata>> _stepLogs = new();

    public Task LogStepAsync(Guid sagaId, Type stepType, StepStatus status, object? payload = null)
    {
        try
        {
            var stepDict = _stepLogs.GetOrAdd(sagaId, _ => new ConcurrentDictionary<string, SagaStepMetadata>());
            var stepName = stepType.ToSagaStepName();

            var messageTypeName = stepType.AssemblyQualifiedName ?? stepName;

            // State transition validation
            if (stepDict.TryGetValue(stepName, out var existingMeta))
            {
                var previousStatus = existingMeta.Status;
                if (!IsValidStepTransition(previousStatus, status))
                {
                    Console.WriteLine(
                        $"Illegal StepStatus transition: {previousStatus} -> {status} for {stepName}");
                    return Task.CompletedTask;
                }
            }

            var metadata = new SagaStepMetadata
            {
                Status = status,
                MessageTypeName = messageTypeName,
                ApplicationId = "InMemory", // Replace with dynamic value if available
                MessagePayload = JsonSerializer.Serialize(payload, payload?.GetType() ?? typeof(object))
            };
            stepDict[stepName] = metadata;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error logging step: {ex}");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsStepCompletedAsync(Guid sagaId, Type stepType)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            var stepName = stepType.ToSagaStepName();

            return Task.FromResult(
                steps.TryGetValue(stepName, out var metadata) && metadata.Status == StepStatus.Completed
            );
        }

        return Task.FromResult(false);
    }

    public Task<StepStatus> GetStepStatusAsync(Guid sagaId, Type stepType)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            steps.TryGetValue(stepType.ToSagaStepName(), out var metadata);

            if (metadata != null) return Task.FromResult(metadata.Status);
        }

        return Task.FromResult(StepStatus.None);
    }

    public Task<IReadOnlyDictionary<string, SagaStepMetadata>> GetSagaStepsAsync(Guid sagaId)
    {
        if (_stepLogs.TryGetValue(sagaId, out var steps))
        {
            return Task.FromResult<IReadOnlyDictionary<string, SagaStepMetadata>>(
                new Dictionary<string, SagaStepMetadata>(steps));
        }

        return Task.FromResult<IReadOnlyDictionary<string, SagaStepMetadata>>(
            new Dictionary<string, SagaStepMetadata>());
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

    public Task<ISagaContext<TStep, TSagaData>> LoadContextAsync<TStep, TSagaData>(Guid sagaId)
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
            data: (TSagaData)data,
            eventBus: eventBus, // Use injected field
            sagaStore: this,
            sagaIdGenerator: sagaIdGenerator // Use injected field
        );

        return Task.FromResult(context);
    }
    
    private static bool IsValidStepTransition(StepStatus previous, StepStatus next)
    {
        return previous switch
        {
            StepStatus.None => next == StepStatus.Started,
            StepStatus.Started => next is StepStatus.Completed or StepStatus.Failed,
            StepStatus.Completed => next == StepStatus.Failed, // Retry or compensation can follow completion
            StepStatus.Failed => next == StepStatus.Compensated,
            StepStatus.Compensated => next == StepStatus.CompensationFailed,
            StepStatus.CompensationFailed => false, // Final state
            _ => false
        };
    }
}