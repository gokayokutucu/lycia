using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Lycia.Messaging;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Enums;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.DependencyInjection;

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
            var stepName =
                stepType.ToSagaStepName(); // Something like "Sample.Messages.Events.OrderShippedEvent, Sample.Messages"

            var messageTypeName = stepType.AssemblyQualifiedName;
            if (string.IsNullOrWhiteSpace(messageTypeName))
            {
                Console.WriteLine(
                    $"⚠️ Warning: AssemblyQualifiedName was null for {stepType.FullName}, using fallback.");
                messageTypeName = stepName;
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
}