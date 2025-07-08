using System.Reflection;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Handlers;
using Lycia.Saga.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Infrastructure.Dispatching;

/// <summary>
/// Responsible for resolving and invoking saga-related handlers for incoming messages.
/// </summary>
public class SagaDispatcher(
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    IEventBus eventBus,
    ISagaCompensationCoordinator sagaCompensationCoordinator,
    IServiceProvider serviceProvider)
    : ISagaDispatcher
{
    private async Task DispatchByMessageTypeAsync<TMessage>(TMessage message) where TMessage : IMessage
    {
        var messageType = message.GetType();

        var startHandlerType = typeof(ISagaStartHandler<>).MakeGenericType(messageType);
        var startHandlers = serviceProvider.GetServices(startHandlerType).ToList();
        if (startHandlers.Count != 0)
        {
            Console.WriteLine($"[Dispatch] Dispatching {messageType.Name} to start handler: {startHandlerType.Name}");
            await InvokeHandlerAsync(startHandlers, message);
        }

        var stepHandlerType = typeof(ISagaHandler<>).MakeGenericType(messageType);
        var stepHandlers = serviceProvider.GetServices(stepHandlerType).ToList();
        if (stepHandlers.Count != 0)
        {
            Console.WriteLine($"[Dispatch] Dispatching {messageType.Name} to step handler: {stepHandlerType.Name}");
            await InvokeHandlerAsync(stepHandlers, message);
        }


        var compensationHandlerType = typeof(ISagaCompensationHandler<>).MakeGenericType(messageType);
        var compensationHandlers = serviceProvider.GetServices(compensationHandlerType).ToList();
        if (compensationHandlers.Count != 0)
        {
            var sagaId = GetSagaId(message);
            await DispatchCompensationHandlersAsync(message, compensationHandlers, sagaId);
        }

// #if UNIT_TESTING
//         if (compensationHandlers.Count == 0 && startHandlers.Count == 0)
//             await LogStep(typeof(NoHandler));
// async Task LogStep(Type handlerType)
// {
//     if (message.__TestStepStatus.HasValue && message.__TestStepType is not null)
//     {
//         Guid sagaId;
//         var sagaIdProperty = message.GetType().GetProperty("SagaId");
//         if (sagaIdProperty != null && sagaIdProperty.GetValue(message) is Guid sagaIdValue && sagaIdValue != Guid.Empty)
//         {
//             sagaId = sagaIdValue;
//         }
//         else
//         {
//             sagaId = sagaIdGenerator.Generate();
//         }
//         
//
//         await sagaStore.LogStepAsync(sagaId, message.MessageId,  message.ParentMessageId, message.__TestStepType, message.__TestStepStatus.Value,
//             handlerType, message);
//     }
// }
// #endif
    }

    /// <summary>
    /// Dispatches the compensation handlers for the given message and saga id.
    /// Initializes handler contexts and invokes compensation methods,
    /// stopping the compensation chain if a failure or exception occurs.
    /// </summary>
    protected virtual async Task DispatchCompensationHandlersAsync<TMessage>(TMessage message, List<object?> handlers,
        Guid sagaId)
        where TMessage : IMessage
    {
        var messageType = message.GetType();

        var handler = handlers.FirstOrDefault();
        if (handler != null)
        {
            var handlerType = handler.GetType();

            await InitializeHandlerContextAsync(handler, message, handlerType, sagaId);

            var continueChain =
                await InvokeCompensationAsync(handler, message, handlerType, sagaId, message.MessageId, messageType);

            if (!continueChain)
                Console.WriteLine("⚡ Compensation chain stopped.");
        }
    }

    /// <summary>
    /// Initializes the context for a compensation handler, injecting the appropriate SagaContext instance.
    /// </summary>
    private async Task
        InitializeHandlerContextAsync(object handler, object message, Type handlerType,
            Guid sagaId) //, Type messageType)
    {
        // Coordinated Compensation
        if (handlerType.IsSubclassOfRawGenericBase(typeof(CoordinatedSagaHandler<,,>)) ||
            handlerType.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,,>)))
        {
            var genericArgs = handlerType.BaseType?.GetGenericArguments();
            var msgType = genericArgs?[0];
            var sagaDataType = genericArgs?[2];

            var contextType = typeof(SagaContext<,>).MakeGenericType(msgType!, sagaDataType!);
            var loadedSagaData =
                await sagaStore.InvokeGenericTaskResultAsync("LoadSagaDataAsync", sagaDataType!, sagaId);
            var contextInstance = Activator.CreateInstance(contextType,
                sagaId,
                message,
                handlerType,
                loadedSagaData,
                eventBus,
                sagaStore,
                sagaIdGenerator,
                sagaCompensationCoordinator);
            var initializeMethod = handlerType.GetMethod("Initialize");
            initializeMethod?.Invoke(handler, [contextInstance]);
        }
        // Reactive Compensation
        else if (handlerType.IsSubclassOfRawGenericBase(typeof(ReactiveSagaHandler<>)) ||
                 handlerType.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>)))
        {
            var genericArgs = handlerType.BaseType?.GetGenericArguments();
            var msgType = genericArgs?[0];

            var contextType = typeof(SagaContext<>).MakeGenericType(msgType!);
            var contextInstance =
                Activator.CreateInstance(contextType,
                    sagaId,
                    message,
                    handlerType,
                    eventBus,
                    sagaStore,
                    sagaIdGenerator,
                    sagaCompensationCoordinator);
            var initializeMethod = handlerType.GetMethod("Initialize");
            initializeMethod?.Invoke(handler, [contextInstance]);
        }
    }

    /// <summary>
    /// Invokes the CompensateAsync method on a compensation handler and checks the saga step status.
    /// Returns false if the compensation chain should break (due to failure or exception), true otherwise.
    /// </summary>
    private async Task<bool> InvokeCompensationAsync(
        object handler,
        object message,
        Type handlerType,
        Guid messageId,
        Guid sagaId,
        Type messageType)
    {
        var methodName =
            handlerType.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>)) ||
            handlerType.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,,>)) || 
            handlerType.IsSubclassOfRawGenericBase(typeof(ReactiveSagaHandler<>)) ||
            handlerType.IsSubclassOfRawGenericBase(typeof(CoordinatedSagaHandler<,,>))
                    ? "CompensateAsyncInternal"
                    // Fallback for ISagaCompensationHandler<> interface implementations
                    : "CompensateAsync";

        // Create a delegate for the compensation method
        var compensateDelegate = HandlerDelegateHelper.GetHandlerDelegate(handlerType, methodName, messageType);

        try
        {
#if UNIT_TESTING
            await LogStep(handlerType);
#endif
            // Call through delegate
            await compensateDelegate(handler, message);
#if UNIT_TESTING
            await LogStep(handlerType);
#endif

            var status = await sagaStore.GetStepStatusAsync(sagaId, messageId, messageType, handlerType);
            if (status == StepStatus.CompensationFailed)
            {
                // Break the compensation chain if compensation failed
                return false;
            }
        }
        catch (Exception ex)
        {
            // Break the compensation chain if an exception occurs (double safety)
            Console.WriteLine("⚡ Compensation chain stopped due to exception.");
            return false;
        }

        return true;

        async Task LogStep(Type logHandlerType)
        {
            if (message is IMessage { __TestStepStatus: not null, __TestStepType: not null } msg)
            {
                Guid sagaIdLocal;
                var sagaIdProperty = message.GetType().GetProperty("SagaId");
                if (sagaIdProperty != null && sagaIdProperty.GetValue(message) is Guid value && value != Guid.Empty)
                {
                    sagaIdLocal = value;
                }
                else
                {
                    sagaIdLocal = sagaIdGenerator.Generate();
                }

                var parentMessageId = (message as IMessage)?.ParentMessageId;
                if (parentMessageId == Guid.Empty)
                    parentMessageId = null;

                await sagaStore.LogStepAsync(sagaIdLocal, messageId, parentMessageId, messageType,
                    StepStatus.Compensated,
                    logHandlerType, message);
            }
        }
    }

    public async Task DispatchAsync<TMessage>(TMessage command) where TMessage : IMessage
    {
        await DispatchByMessageTypeAsync(command);
    }

    public async Task DispatchAsync<TMessage, TResponse>(TResponse message)
        where TMessage : IMessage
        where TResponse : IResponse<TMessage>
    {
        var messageType = message.GetType();

        if (typeof(IResponse<>).IsAssignableFrom(messageType) &&
            (IsEvent(messageType) || IsCommand(messageType)))
        {
            return;
        }

        if (IsSuccessResponse(messageType))
        {
            var handlerType = typeof(ISuccessResponseHandler<>).MakeGenericType(messageType);
            Console.WriteLine($"[Dispatch] Dispatching {messageType.Name} to {handlerType.Name}");
            await InvokeHandlerAsync(serviceProvider.GetServices(handlerType), message);
        }
        else if (IsFailResponse(messageType))
        {
            var handlerType = typeof(IFailResponseHandler<>).MakeGenericType(messageType);
            var fail = new FailResponse
            {
                Reason = "An error occurred while handling the message.",
                ExceptionType = message.GetType().Name,
                OccurredAt = DateTime.UtcNow
            };
            Console.WriteLine($"[Dispatch] Dispatching {messageType.Name} to {handlerType.Name}");
            await InvokeHandlerAsync(serviceProvider.GetServices(handlerType), message, fail);
        }
        else
        {
            await DispatchByMessageTypeAsync(message);
        }
    }

    protected virtual async Task InvokeHandlerAsync(
        IEnumerable<object?> handlers,
        IMessage message,
        FailResponse? fail = null)
    {
        foreach (var handler in handlers)
        {
            // SagaId resolution logic
            var messageType = message.GetType();
            var sagaIdProp = messageType.GetProperty("SagaId");
            Guid sagaId;

            // Only ISagaStartHandler gets a new SagaId if needed
            var handlerType = handler!.GetType();
            var isStartHandler = handlerType.IsSubclassOfRawGeneric(typeof(ISagaStartHandler<>));

            if (sagaIdProp != null && sagaIdProp.GetValue(message) is Guid value && value != Guid.Empty)
            {
                sagaId = value;
            }
            else if (isStartHandler)
            {
                sagaId = sagaIdGenerator.Generate();
                // Optionally assign to message property if settable
                if (sagaIdProp != null && sagaIdProp.CanWrite)
                    sagaIdProp.SetValue(message, sagaId);
            }
            else
            {
                // Not a start handler and SagaId missing: throw!
                throw new InvalidOperationException("Missing SagaId on a non-starting message.");
            }

            // Coordinated (with SagaData)
            if (handlerType.IsSubclassOfRawGenericBase(typeof(CoordinatedSagaHandler<,,>)) ||
                handlerType.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,,>)))
            {
                var genericArgs = handlerType.BaseType?.GetGenericArguments();
                var msgType = genericArgs?[0];
                var sagaDataType = genericArgs?[2];

                // Create SagaContext with loaded SagaData: SagaContext<msgType, sagaDataType>
                var contextType = typeof(SagaContext<,>).MakeGenericType(msgType!, sagaDataType!);
                var loadedSagaData =
                    await sagaStore.InvokeGenericTaskResultAsync("LoadSagaDataAsync", sagaDataType!, sagaId);
                var contextInstance = Activator.CreateInstance(contextType,
                    sagaId,
                    message,
                    handlerType,
                    loadedSagaData,
                    eventBus,
                    sagaStore,
                    sagaIdGenerator,
                    sagaCompensationCoordinator);
                var initializeMethod = handlerType.GetMethod("Initialize");
                initializeMethod?.Invoke(handler, [contextInstance]);

                await HandleSagaAsync(handler, handlerType);
                continue;
            }

            // Reactive (no SagaData)
            if (handlerType.IsSubclassOfRawGenericBase(typeof(ReactiveSagaHandler<>)) ||
                handlerType.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>)))
            {
                var genericArgs = handlerType.BaseType?.GetGenericArguments();
                var msgType = genericArgs?[0];

                var contextType = typeof(SagaContext<>).MakeGenericType(msgType!);
                var contextInstance =
                    Activator.CreateInstance(contextType,
                        sagaId,
                        message,
                        handlerType,
                        eventBus,
                        sagaStore,
                        sagaIdGenerator,
                        sagaCompensationCoordinator);
                var initializeMethod = handlerType.GetMethod("Initialize");
                initializeMethod?.Invoke(handler, [contextInstance]);

                await HandleSagaAsync(handler, handlerType);
            }
        }

        async Task HandleSagaAsync(object? handler, Type handlerType)
        {
            if (handler == null) return;

            // if the handler is a StartCoordinatedSagaHandler or StartReactiveSagaHandler, call HandleStartAsync
            if (handlerType.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>))
                || handlerType.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,,>)))
            {
                // Call HandleStartAsync
                try
                {
                    var sagaId = GetSagaId(message);

                    var delegateMethod =
                        HandlerDelegateHelper.GetHandlerDelegate(handlerType, "HandleAsyncInternal", message.GetType());
                    await delegateMethod(handler, message);

                    await ValidateSagaStepCompletionAsync(message, handlerType, sagaId);
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
                {
                    // Optionally log or throw a descriptive error
                    throw new InvalidOperationException($"Failed to invoke HandleAsync dynamically: {ex.Message}", ex);
                }
            }
            else
            {
                // Call HandleAsync for other handlers
                try
                {
                    var sagaId = GetSagaId(message);

                    var delegateMethod =
                        HandlerDelegateHelper.GetHandlerDelegate(handlerType, "HandleAsyncInternal", message.GetType());
                    await delegateMethod(handler, message);

                    await ValidateSagaStepCompletionAsync(message, handlerType, sagaId);
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
                {
                    // Optionally log or throw a descriptive error
                    throw new InvalidOperationException($"Failed to invoke HandleAsync dynamically: {ex.Message}", ex);
                }
            }
        }
    }

    private Guid GetSagaId(IMessage message)
    {
        Guid sagaId;
        var sagaIdProp = message.GetType().GetProperty("SagaId");
        if (sagaIdProp != null && sagaIdProp.GetValue(message) is Guid value && value != Guid.Empty)
        {
            sagaId = value;
        }
        else
        {
            sagaId = sagaIdGenerator.Generate();
        }

        return sagaId;
    }

    private async Task ValidateSagaStepCompletionAsync(IMessage message, Type handlerType, Guid sagaId)
    {
        var stepTypeToCheck = message.GetType();
        var alreadyMarked =
            await sagaStore.IsStepCompletedAsync(sagaId, message.MessageId, stepTypeToCheck, handlerType);
        if (!alreadyMarked)
        {
            Console.WriteLine($"Step for {stepTypeToCheck.Name} was not marked as completed, failed, or compensated.");
            // If you want to throw an exception, uncomment the line below:
            // throw new InvalidOperationException($"Step {stepTypeToCheck.Name} was not marked as completed/failed/compensated.");
        }
    }

    private static bool IsSuccessResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISuccessResponse<>));

    private static bool IsFailResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IFailResponse<>));

    private static bool IsCommand(Type type) => typeof(ICommand).IsAssignableFrom(type);
    private static bool IsEvent(Type type) => typeof(IEvent).IsAssignableFrom(type);
}