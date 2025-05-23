using System.Text.Json;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Enums;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Infrastructure.Compensating;

public class SagaCompensationCoordinator
{
    private readonly ISagaStore _sagaStoreField;
    private readonly ISagaIdGenerator _sagaIdGeneratorField; // Will be used when context init is added
    private readonly IEventBus _eventBusField;             // Will be used when context init is added
    private readonly IServiceProvider _serviceProvider;

    public SagaCompensationCoordinator(
        ISagaStore sagaStore,
        ISagaIdGenerator sagaIdGenerator,
        IEventBus eventBus,
        IServiceProvider serviceProvider)
    {
        _sagaStoreField = sagaStore;
        _sagaIdGeneratorField = sagaIdGenerator;
        _eventBusField = eventBus;
        _serviceProvider = serviceProvider;
    }
    
    public async Task TriggerCompensationAsync(Guid sagaId, string failedStep)
    {
        var steps = await _sagaStoreField.GetSagaStepsAsync(sagaId);
        var orderedSteps = steps
            .OrderByDescending(x => x.Value.RecordedAt)
            .ToList(); //Take a snapshot

        foreach (var step in orderedSteps)
        {
            string stepName = step.Key;
            var stepMetadata = step.Value;
            
            // Skip the failed step itself — compensation should apply only to previously completed steps
            if (stepName == failedStep)
            {
                Console.WriteLine($"[Compensation] Skipping failed step: {stepName}");
                continue;
            }

            if (stepMetadata.Status != StepStatus.Completed)
            {
                Console.WriteLine($"[Compensation] Skipping incomplete step: {stepName} with status {stepMetadata.Status}");
                continue;
            }

            var messageType = stepMetadata.MessageTypeName.TryResolveSagaStepType();
            if (messageType == null) continue;

            var handlerType = typeof(ISagaCompensationHandler<>).MakeGenericType(messageType);
            var handlers = _serviceProvider.GetServices(handlerType); // Use injected _serviceProvider

            foreach (var handler in handlers)
            {
                // Context initialization logic (which uses _eventBusField, _sagaStoreField, _sagaIdGeneratorField) 
                // would go here, as implemented in a previous subtask.
                // For this refactoring, we are ensuring the fields are available.

                var method = handlerType.GetMethod("CompensateAsync");
                if (method == null) continue;

                var payload = JsonSerializer.Deserialize(stepMetadata.MessagePayload, messageType);

                try
                {
                    await (Task)method.Invoke(handler, [payload])!;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Compensation handler failed for {stepName}: {ex.Message}");

                    // Mark the step as having failed during compensation
                    await _sagaStoreField.LogStepAsync(sagaId, messageType, StepStatus.CompensationFailed, payload);
                }
            }
        }
    }
}