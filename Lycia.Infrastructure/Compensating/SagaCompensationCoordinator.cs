using System.Text.Json;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Enums;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Infrastructure.Compensating;


public class SagaCompensationCoordinator(IServiceProvider serviceProvider)
{
    private ISagaStore SagaStore => _sagaStore ??= serviceProvider.GetRequiredService<ISagaStore>();
    private ISagaStore? _sagaStore;   
    
    public async Task TriggerCompensationAsync(Guid sagaId, string failedStep)
    {
        var steps = await SagaStore.GetSagaStepsAsync(sagaId);
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
            var handlers = serviceProvider.GetServices(handlerType);

            foreach (var handler in handlers)
            {
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
                    await SagaStore.LogStepAsync(sagaId, messageType, StepStatus.CompensationFailed, payload);
                }
            }
        }
    }
}