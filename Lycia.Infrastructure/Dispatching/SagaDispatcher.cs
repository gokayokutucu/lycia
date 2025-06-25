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
            //return;
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
// #endif

        async Task LogStep(Type handlerType)
        {
            if (message.__TestStepStatus.HasValue && message.__TestStepType is not null)
            {
                Guid sagaId;
                var sagaIdProperty = message.GetType().GetProperty("SagaId");
                if (sagaIdProperty != null && sagaIdProperty.GetValue(message) is Guid value && value != Guid.Empty)
                {
                    sagaId = value;
                }
                else
                {
                    sagaId = sagaIdGenerator.Generate();
                }

                await sagaStore.LogStepAsync(sagaId, message.__TestStepType, message.__TestStepStatus.Value,
                    handlerType, message);
            }
        }
    }

    /// <summary>
    /// Compensation handler discovery strategy:
    /// Gathers all registered ISagaStartHandler<>, ISagaHandler<>, and ISagaCompensationHandler<> for the current message type,
    /// then filters for handlers whose type is a subclass of one of the four saga handler base types:
    ///   - StartReactiveSagaHandler<>
    ///   - ReactiveSagaHandler<>
    ///   - StartCoordinatedSagaHandler<,,>
    ///   - CoordinatedSagaHandler<,,>
    /// or implements ISagaCompensationHandler<>.
    /// Uses DistinctBy(x => x.GetType()) to prevent duplicates.
    /// </summary>
    private List<object?> FindCompensationHandlers<TMessage>(Type messageType) where TMessage : IMessage
    {
        var allSagaHandlerTypes = new[]
        {
            typeof(ISagaStartHandler<>),
            typeof(ISagaHandler<>),
            typeof(ISagaCompensationHandler<>)
        };
        var allHandlers = new List<object?>();
        foreach (var handlerInterface in allSagaHandlerTypes)
        {
            var closedType = handlerInterface.MakeGenericType(messageType);
            allHandlers.AddRange(serviceProvider.GetServices(closedType));
        }
        var compensationHandlers = allHandlers
            .Where(handler =>
            {
                if (handler is null) return false;
                var ht = handler.GetType();
                // Check if subclass of any of the four saga handler base types
                if (ht.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>)) ||
                    ht.IsSubclassOfRawGenericBase(typeof(ReactiveSagaHandler<>)) ||
                    ht.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,,>)) ||
                    ht.IsSubclassOfRawGenericBase(typeof(CoordinatedSagaHandler<,,>)))
                {
                    return true;
                }
                // Check if implements ISagaCompensationHandler<>
                var compensationInterface = typeof(ISagaCompensationHandler<>).MakeGenericType(messageType);
                if (compensationInterface.IsAssignableFrom(ht))
                {
                    return true;
                }
                return false;
            })
            .DistinctBy(x => x!.GetType())
            .ToList();
        return compensationHandlers;
    }

    /// <summary>
    /// Dispatches the compensation handlers for the given message and saga id.
    /// Initializes handler contexts and invokes compensation methods,
    /// stopping the compensation chain if a failure or exception occurs.
    /// </summary>
    private async Task DispatchCompensationHandlersAsync<TMessage>(TMessage message, List<object?> handlers,
        Guid sagaId)
        where TMessage : IMessage
    {
        var messageType = message.GetType();

        foreach (var handler in handlers)
        {
            var handlerType = handler!.GetType();

            await InitializeHandlerContextAsync(handler, handlerType, sagaId); //, messageType);

            var continueChain = await InvokeCompensationAsync(handler, message, handlerType, sagaId, messageType);
            if (continueChain) continue;
            Console.WriteLine("⚡ Compensation chain stopped.");
            break;
        }
    }

    /// <summary>
    /// Initializes the context for a compensation handler, injecting the appropriate SagaContext instance.
    /// </summary>
    private async Task
        InitializeHandlerContextAsync(object handler, Type handlerType, Guid sagaId) //, Type messageType)
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
    private async Task<bool> InvokeCompensationAsync(object handler, object message, Type handlerType, Guid sagaId,
        Type messageType)
    {
        MethodInfo? method;

        if (handlerType.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>)) ||
            handlerType.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,,>)))
        {
            method = handlerType.GetMethod("CompensateStartAsync");
        }
        else if (handlerType.IsSubclassOfRawGenericBase(typeof(ReactiveSagaHandler<>)) ||
                 handlerType.IsSubclassOfRawGenericBase(typeof(CoordinatedSagaHandler<,,>)))
        {
            method = handlerType.GetMethod("CompensateAsync");
        }
        else
        {
            // Fallback for interface-based handler
            var compensationInterface = typeof(ISagaCompensationHandler<>).MakeGenericType(messageType);
            method = compensationInterface.GetMethod("CompensateAsync")
                     ?? handlerType.GetMethod("CompensateAsync");
        }

        if (method == null) return true;

        try
        {
#if UNIT_TESTING
            await LogStep(handlerType);
#endif
            await (Task)method.Invoke(handler, [message])!;
#if UNIT_TESTING
            await LogStep(handlerType);
#endif

            var status = await sagaStore.GetStepStatusAsync(sagaId, messageType, handlerType);
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

                await sagaStore.LogStepAsync(sagaIdLocal, messageType, StepStatus.Compensated,
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

    private async Task InvokeHandlerAsync(
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
                        handlerType, 
                        eventBus, 
                        sagaStore, 
                        sagaIdGenerator,
                        sagaCompensationCoordinator);
                var initializeMethod = handlerType.GetMethod("Initialize");
                initializeMethod?.Invoke(handler, [contextInstance]);

                await HandleSagaAsync(handler, handlerType);
                continue;
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
                    dynamic dynamicHandler = handler;

                    var sagaId = GetSagaId(message);

                    await dynamicHandler.HandleAsyncInternal((dynamic)message);

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
                    dynamic dynamicHandler = handler;

                    var sagaId = GetSagaId(message);

                    await dynamicHandler.HandleAsyncInternal((dynamic)message);

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
        var alreadyMarked = await sagaStore.IsStepCompletedAsync(sagaId, stepTypeToCheck, handlerType);
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