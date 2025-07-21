using Lycia.Infrastructure.Extensions;
using Lycia.Infrastructure.Helpers;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga;
using Lycia.Saga.Handlers;
using Lycia.Saga.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using MoreLinq;

// Added for SagaContext<>

namespace Lycia.Infrastructure.Compensating;

public class SagaCompensationCoordinator(IServiceProvider serviceProvider) : ISagaCompensationCoordinator
{
    /// <summary>
    /// Compensates the saga steps corresponding to the specified failed step type.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga.</param>
    /// <param name="failedStepType">The type of the failed step to compensate.</param>
    public async Task CompensateAsync(Guid sagaId, Type failedStepType)
    {
        try
        {
            if (serviceProvider.GetService(typeof(ISagaStore)) is not ISagaStore sagaStore) return;

            var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId);

            // Filter steps to find those that match the failed step type and have a status of Failed
            var failedSteps = steps
                .Where(s =>
                    s.Value.MessageTypeName == failedStepType.AssemblyQualifiedName &&
                    s.Value.Status == StepStatus.Failed)
                .ToList();

            foreach (var step in failedSteps)
            {
                var stepType = Type.GetType(step.Key.stepType);
                if (stepType == null) continue;

                var payloadType = Type.GetType(step.Value.MessageTypeName);
                if (payloadType == null) continue;

                var messageObject = JsonConvert.DeserializeObject(step.Value.MessagePayload, payloadType);
                if (messageObject == null) continue;

                // Try to get the handler via ISagaCompensationHandler<>
                var onlySagaCompensationHandlerType = typeof(ISagaCompensationHandler<>).MakeGenericType(stepType);
                // If no handler found, try to find candidate handlers using the new method
                var handlers = serviceProvider.GetServices(onlySagaCompensationHandlerType).Cast<object>()
                    .Concat(SagaHandlerHelper.FindCompensationHandlers(serviceProvider, stepType)) // ISagaCompensationHandler<> + ISagaStartHandler<> and ISagaHandler<>
                    .DistinctByKey(h => h.GetType())
                    .ToList();

                foreach (var handler in handlers)
                {
                    var handlerBaseType = handler.GetType().BaseType;

                    // Determine the method name and delegate based on handler base type or interface
                    Delegate? compensationDelegate = null;
                    var handlerTypeActual = handler.GetType();

                    var handlerGenericDef =
                        handlerBaseType != null && handlerBaseType.IsGenericType
                            ? handlerBaseType.GetGenericTypeDefinition()
                            : null;

                    if (handlerGenericDef == typeof(StartReactiveSagaHandler<>) ||
                        handlerGenericDef == typeof(StartCoordinatedSagaHandler<,,>) ||
                        handlerGenericDef == typeof(ReactiveSagaHandler<>) ||
                        handlerGenericDef == typeof(CoordinatedSagaHandler<,,>))
                    {
                        // Use "CompensateAsyncInternal" for these base types
                        compensationDelegate = HandlerDelegateHelper.GetHandlerDelegate(handlerTypeActual, "CompensateAsyncInternal", stepType);
                    }
                    else
                    {
                        // Check if handler implements ISagaCompensationHandler<> for stepType
                        var implementsCompensationHandler = handlerTypeActual.GetInterfaces().Any(i =>
                            i.IsGenericType &&
                            i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>) &&
                            i.GetGenericArguments()[0] == stepType);

                        if (implementsCompensationHandler)
                        {
                            // Use "CompensateAsync" for ISagaCompensationHandler<>
                            compensationDelegate = HandlerDelegateHelper.GetHandlerDelegate(handlerTypeActual, "CompensateAsync", stepType);
                        }
                    }

                    if (compensationDelegate == null)
                    {
                        // No suitable delegate found, skip this handler
                        continue;
                    }

                    // Invoke the compensation delegate asynchronously
                    // This calls the appropriate compensation method on the handler
                    await (Task)compensationDelegate.DynamicInvoke(handler, messageObject)!;
                }
            }
        }
        catch (Exception ex)
        {
            // Log exception here (e.g., using ILogger) before rethrowing
            throw;
        }
    }

    /// <summary>
    /// Compensates the parent saga step of the specified step type.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga.</param>
    /// <param name="stepType">The type of the step whose parent is to be compensated.</param>
    /// <param name="message">The message of the current step</param>
    public async Task CompensateParentAsync(Guid sagaId, Type stepType, IMessage message)
    {
        try
        {
            if (serviceProvider.GetService(typeof(ISagaStore)) is not ISagaStore sagaStore)
                return;

            var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId);

            //var parentChain = SagaStepHelper.GetParentChain(steps.Values, message.MessageId);

            // Get the parent message ID from the current message
            var parentMessageId = message.ParentMessageId;
            if (parentMessageId == Guid.Empty)
                return;

            // Find the parent step in the saga steps(with MessageId)
            var parentStep = steps.FirstOrDefault(meta =>
                meta.Value.MessageId == parentMessageId);

            if (parentStep.Equals(default(KeyValuePair<(string, string, string), SagaStepMetadata>)))
                return;

            var parentStepType = Type.GetType(parentStep.Key.stepType);
            if (parentStepType == null)
                return;

            var payloadType = Type.GetType(parentStep.Value.MessageTypeName);
            if (payloadType == null)
                return;

            var messageObject = JsonConvert.DeserializeObject(parentStep.Value.MessagePayload, payloadType);
            if (messageObject == null)
                return;

            // Find the compensation handler for the parent step type
            var onlySagaCompensationHandlerType = typeof(ISagaCompensationHandler<>).MakeGenericType(parentStepType);
            var handlers = serviceProvider.GetServices(onlySagaCompensationHandlerType);
            var handler = handlers.Cast<object>()
                              .FirstOrDefault(h => h.GetType().FullName == parentStep.Key.handlerType)
                          ?? SagaHandlerHelper.FindCompensationHandlers(serviceProvider, parentStepType)
                              .FirstOrDefault(h => h.GetType().FullName == parentStep.Key.handlerType);


            if (handler == null)
                return;

            var handlerBaseType = handler.GetType().BaseType;
            var handlerGenericDef = handlerBaseType?.IsGenericType == true
                ? handlerBaseType.GetGenericTypeDefinition()
                : null;

            Delegate? compensationDelegate = null;
            var handlerTypeActual = handler.GetType();

            if (handlerGenericDef == typeof(StartReactiveSagaHandler<>) ||
                handlerGenericDef == typeof(StartCoordinatedSagaHandler<,,>) ||
                handlerGenericDef == typeof(ReactiveSagaHandler<>) ||
                handlerGenericDef == typeof(CoordinatedSagaHandler<,,>))
            {
                // Use "CompensateAsyncInternal" for these base types
                compensationDelegate = HandlerDelegateHelper.GetHandlerDelegate(handlerTypeActual, "CompensateAsyncInternal", parentStepType);
            }
            else
            {
                // Check if handler implements ISagaCompensationHandler<> for parentStepType
                var implementsCompensationHandler = handlerTypeActual.GetInterfaces().Any(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>) &&
                    i.GetGenericArguments()[0] == parentStepType);

                if (implementsCompensationHandler)
                {
                    // Use "CompensateAsync" for ISagaCompensationHandler<>
                    compensationDelegate = HandlerDelegateHelper.GetHandlerDelegate(handlerTypeActual, "CompensateAsync", parentStepType);
                }
            }

            if (compensationDelegate == null)
                return;

            // Invoke the compensation delegate asynchronously
            // This calls the appropriate compensation method on the handler
            await (Task)compensationDelegate.DynamicInvoke(handler, messageObject)!;
        }
        catch (Exception ex)
        {
            // Log ve error handle the exception appropriately
            throw;
        }
    }
}