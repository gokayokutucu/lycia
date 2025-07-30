using System.Reflection;
using Lycia.Infrastructure.Extensions;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga;
using Lycia.Saga.Extensions;
using Lycia.Saga.Handlers;
using Lycia.Saga.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Lycia.Infrastructure.Compensating;

public class SagaCompensationCoordinator(IServiceProvider serviceProvider, ISagaIdGenerator sagaIdGenerator)
    : ISagaCompensationCoordinator
{
    /// <summary>
    /// Compensates the saga steps corresponding to the specified failed step type.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga.</param>
    /// <param name="failedStepType">The type of the failed step to compensate.</param>
    /// <param name="handlerType"></param>
    /// <param name="message"></param>
    public async Task CompensateAsync(Guid sagaId, Type failedStepType, Type? handlerType, IMessage message)
    {
        if (handlerType == null) return;

        if (serviceProvider.GetService(typeof(IEventBus)) is not IEventBus eventBus)
            throw new InvalidOperationException("IEventBus not resolved.");

        if (serviceProvider.GetService(typeof(ISagaStore)) is not ISagaStore
            sagaStore)
            throw new InvalidOperationException("ISagaStore not resolved.");

        var stepKeyValuePair = await sagaStore.GetSagaHandlerStepAsync(sagaId, message.MessageId);
        if (IsStepAlreadyInStatus(stepKeyValuePair, StepStatus.Failed, StepStatus.Compensated, StepStatus.CompensationFailed))
            return;

        await sagaStore.LogStepAsync(sagaId, message.MessageId, message.ParentMessageId, failedStepType,
            StepStatus.Failed, handlerType, message);

        stepKeyValuePair = await sagaStore.GetSagaHandlerStepAsync(sagaId, message.MessageId);
        if (!stepKeyValuePair.HasValue) return;
        var step = stepKeyValuePair.Value;

        var stepType = Type.GetType(step.Key.stepType);
        var payloadType = Type.GetType(step.Value.MessageTypeName);
        if (stepType == null || payloadType == null) return;

        var messageObject = JsonConvert.DeserializeObject(step.Value.MessagePayload, payloadType);
        if (messageObject == null) return;

        // If no handler found, try to find candidate handlers using the new method
        var handler = FindCompensationHandler(handlerType, stepType);

        await InvokeCompensationHandlerAsync(sagaId, handler, stepType, sagaStore, messageObject, eventBus);
    }


    /// <summary>
    /// Compensates the parent saga step of the specified step type.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga.</param>
    /// <param name="stepType">The type of the step whose parent is to be compensated.</param>
    /// <param name="handlerType"></param>
    /// <param name="message">The message of the current step</param>
    public async Task CompensateParentAsync(Guid sagaId, Type stepType, Type handlerType, IMessage message)
    {
        if (serviceProvider.GetService(typeof(IEventBus)) is not IEventBus eventBus)
            throw new InvalidOperationException("IEventBus not resolved.");

        if (serviceProvider.GetService(typeof(ISagaStore)) is not ISagaStore
            sagaStore)
            throw new InvalidOperationException("ISagaStore not resolved.");

        var stepKeyValuePair = await sagaStore.GetSagaHandlerStepAsync(sagaId, message.MessageId);
        if (IsStepAlreadyInStatus(stepKeyValuePair, StepStatus.Compensated, StepStatus.CompensationFailed))
            return;
        // Log the step as failed before compensating
        await sagaStore.LogStepAsync(sagaId, message.MessageId, message.ParentMessageId, stepType,
            StepStatus.Compensated, handlerType, message);

        stepKeyValuePair = await sagaStore.GetSagaHandlerStepAsync(sagaId, message.MessageId);
        if (!stepKeyValuePair.HasValue) return;

        // Get the parent message ID from the current message
        var parentMessageId = message.ParentMessageId;
        if (parentMessageId == Guid.Empty)
            return;

        // Find the parent step in the saga steps(with MessageId)
        var parentStepKeyValuePair = await sagaStore.GetSagaHandlerStepAsync(sagaId, message.ParentMessageId);

        if (!parentStepKeyValuePair.HasValue) return;

        var parentStep = parentStepKeyValuePair.Value;

        var parentStepType = Type.GetType(parentStep.Key.stepType);
        if (parentStepType == null)
            return;

        var messageObject = JsonConvert.DeserializeObject(parentStep.Value.MessagePayload, parentStepType);
        if (messageObject == null)
            return;

        var parentHandlerType = Type.GetType(parentStep.Key.handlerType);
        // Find the compensation handler for the parent step type
        var handler =
            FindCompensationHandler(parentHandlerType, parentStepType);

        await InvokeCompensationHandlerAsync(sagaId, handler, parentStepType, sagaStore, messageObject, eventBus);
    }

    private async Task InvokeCompensationHandlerAsync(Guid sagaId, object? handler, Type stepType, ISagaStore sagaStore,
        object messageObject, IEventBus eventBus)
    {
        if (handler == null)
            return;

        // Determine the method name and delegate based on handler base type or interface
        Delegate? compensationDelegate = null;
        var handlerTypeActual = handler.GetType();

        var handlerBaseType = handler.GetType().BaseType;
        var handlerGenericDef =
            handlerBaseType is { IsGenericType: true }
                ? handlerBaseType.GetGenericTypeDefinition()
                : null;

        if (handlerGenericDef == typeof(StartReactiveSagaHandler<>) ||
            handlerGenericDef == typeof(StartCoordinatedSagaHandler<,,>) ||
            handlerGenericDef == typeof(ReactiveSagaHandler<>) ||
            handlerGenericDef == typeof(CoordinatedSagaHandler<,,>))
        {
            // Use "CompensateAsyncInternal" for these base types
            compensationDelegate =
                HandlerDelegateHelper.GetHandlerDelegate(handlerTypeActual, "CompensateAsyncInternal",
                    stepType);
        }
        else
        {
            // Check if handler implements ISagaCompensationHandler<> for stepType
            var implementsCompensationHandler = handlerTypeActual.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>) &&
                i.GetGenericArguments()[0].FullName == stepType.FullName);

            if (implementsCompensationHandler)
            {
                // Use "CompensateAsync" for ISagaCompensationHandler<>
                compensationDelegate =
                    HandlerDelegateHelper.GetHandlerDelegate(handlerTypeActual, "CompensateAsync",
                        stepType);
            }
        }

        if (compensationDelegate == null)
        {
            // No suitable delegate found, skip this handler
            return;
        }

        // ---- CONTEXT INITIALIZATION ----
        var (initializeMethod, contextInstance) = await InitializeSagaContext(sagaId, handlerTypeActual,
            handlerGenericDef, handlerBaseType, sagaStore, messageObject, eventBus);

        if (contextInstance != null && initializeMethod != null)
            initializeMethod.Invoke(handler, [contextInstance]);

        // ---- INVOKE COMPENSATION ----
        // Invoke the compensation delegate asynchronously
        // This calls the appropriate compensation method on the handler
        await (Task)compensationDelegate.DynamicInvoke(handler, messageObject)!;
    }

    private async Task<(MethodInfo? initializeMethod, object? contextInstance)> InitializeSagaContext(Guid sagaId,
        Type handlerTypeActual, Type? handlerGenericDef,
        Type? handlerBaseType, ISagaStore sagaStore, object messageObject, IEventBus eventBus)
    {
        var initializeMethod = handlerTypeActual.GetMethod("Initialize");
        object? contextInstance = null;
        if (handlerGenericDef == typeof(CoordinatedSagaHandler<,,>) ||
            handlerGenericDef == typeof(StartCoordinatedSagaHandler<,,>))
        {
            var genericArgs = handlerBaseType!.GetGenericArguments();
            var msgType = genericArgs[0];
            var sagaDataType = genericArgs[2];
            var loadedSagaData =
                await sagaStore.InvokeGenericTaskResultAsync("LoadSagaDataAsync", sagaDataType, sagaId);
            var contextType = typeof(SagaContext<,>).MakeGenericType(msgType, sagaDataType);
            contextInstance = Activator.CreateInstance(contextType, sagaId, messageObject,
                handlerTypeActual, loadedSagaData, eventBus, sagaStore, sagaIdGenerator, this);
        }
        else if (handlerGenericDef == typeof(ReactiveSagaHandler<>) ||
                 handlerGenericDef == typeof(StartReactiveSagaHandler<>))
        {
            var genericArgs = handlerBaseType!.GetGenericArguments();
            var msgType = genericArgs[0];
            var contextType = typeof(SagaContext<>).MakeGenericType(msgType);
            contextInstance = Activator.CreateInstance(contextType, sagaId, messageObject,
                handlerTypeActual, eventBus, sagaStore, sagaIdGenerator, this);
        }

        return (initializeMethod, contextInstance);
    }

    private object? FindCompensationHandler(Type? handlerType, Type stepType)
    {
        if (handlerType == null) return null;

        var interfaceType = typeof(ISagaCompensationHandler<>).MakeGenericType(stepType);

        var interfaceHandlers = serviceProvider.GetServices(interfaceType).Cast<object>();
        var concreteHandler = serviceProvider.GetService(handlerType);

        var allHandlers = concreteHandler != null
            ? interfaceHandlers.Concat([concreteHandler])
            : interfaceHandlers;

        return allHandlers
            .DistinctByKey(h => h.GetType())
            .FirstOrDefault(t => t?.GetType().FullName == handlerType.FullName);
    }

    private static bool IsStepAlreadyInStatus(
        KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>? stepKeyValuePair,
        params StepStatus[] statuses)
    {
        return stepKeyValuePair.HasValue && statuses.Contains(stepKeyValuePair.Value.Value.Status);
    }
}