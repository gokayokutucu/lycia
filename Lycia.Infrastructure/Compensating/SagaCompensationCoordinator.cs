using Newtonsoft.Json;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Lycia.Saga; // Added for SagaContext<>

namespace Lycia.Infrastructure.Compensating;

public class SagaCompensationCoordinator(
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    IEventBus eventBus,
    IServiceProvider serviceProvider)
{
    public async Task TriggerCompensationAsync(Guid sagaId, (string sagaType, string handlerType) failedStep)
    {
        var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId); // Use injected
        var orderedSteps = steps
            .OrderByDescending(x => x.Value.RecordedAt)
            .ToList(); //Take a snapshot

        foreach (var step in orderedSteps)
        {
            (string sagaType, string handlerType) stepKey = step.Key;
            var stepMetadata = step.Value;
            
            // Skip the failed step itself — compensation should apply only to previously completed steps
            if (stepKey == failedStep)
            {
                Console.WriteLine($"[Compensation] Skipping failed step: {stepKey}");
                continue;
            }

            if (stepMetadata.Status != StepStatus.Completed)
            {
                Console.WriteLine($"[Compensation] Skipping incomplete step: {stepKey} with status {stepMetadata.Status}");
                continue;
            }

            var messageType = stepMetadata.MessageTypeName.TryResolveSagaStepType();
            if (messageType == null) continue;

            var handlerType = typeof(ISagaCompensationHandler<>).MakeGenericType(messageType);
            var handlers = serviceProvider.GetServices(handlerType); // Use injected

            foreach (var handler in handlers)
            {
                if (handler == null) continue;

                var payload = JsonConvert.DeserializeObject(stepMetadata.MessagePayload, messageType);
                if (payload == null) 
                {
                    Console.WriteLine($"[Compensation] Error: Failed to deserialize payload for step {stepKey}, message type {messageType.Name}.");
                    continue;
                }

                try
                {
                    // Context Initialization for ISagaCompensationHandler
                    var contextGenericType = typeof(SagaContext<>).MakeGenericType(messageType);
                    var contextConstructor = contextGenericType.GetConstructor(new[] { typeof(Guid), typeof(IEventBus), typeof(ISagaStore), typeof(ISagaIdGenerator)
                    });
                    
                    if (contextConstructor != null)
                    {
                        var context = contextConstructor.Invoke(new object[] { sagaId, eventBus, sagaStore, sagaIdGenerator });
                        var initializeMethodInfo = handler.GetType().GetMethod("Initialize", new[] { typeof(ISagaContext<>).MakeGenericType(messageType)
                        });
                        initializeMethodInfo?.Invoke(handler, new[] { context });
                        Console.WriteLine($"[Compensation] Initialized ISagaContext for handler {handler.GetType().Name} with SagaId {sagaId}");
                    }
                    else
                    {
                         Console.WriteLine($"[Compensation] Error: Could not find suitable constructor for SagaContext<{messageType.Name}> for handler {handler.GetType().Name}.");
                    }

                    // Invoke CompensateAsync
                    var compensateMethod = handlerType.GetMethod("CompensateAsync");
                    if (compensateMethod == null) 
                    {
                        Console.WriteLine($"[Compensation] Error: CompensateAsync method not found on handler {handler.GetType().Name}.");
                        continue;
                    }
                    
                    await (Task)compensateMethod.Invoke(handler, new[] { payload })!;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Compensation handler failed for {stepKey}: {ex.Message}");

                    // Mark the step as having failed during compensation
                    await sagaStore.LogStepAsync(sagaId, messageType, StepStatus.CompensationFailed,  handler.GetType(), payload); // Use injected
                }
            }
        }
    }
}