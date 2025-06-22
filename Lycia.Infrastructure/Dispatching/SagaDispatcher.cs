using Lycia.Infrastructure.Abstractions;
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
            
            foreach (var handler in compensationHandlers)
            {
                var handlerType = handler?.GetType();

                // Coordinated Compensation
                if (handlerType.IsSubclassOfRawGenericBase(typeof(CoordinatedSagaHandler<,,>)) ||
                    handlerType.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,,>)))
                {
                    var genericArgs = handlerType?.BaseType?.GetGenericArguments();
                    var msgType = genericArgs?[0];
                    var sagaDataType = genericArgs?[2];

                    var contextType = typeof(SagaContext<,>).MakeGenericType(msgType!, sagaDataType!);
                    var loadedSagaData = await sagaStore.InvokeGenericTaskResultAsync("LoadSagaDataAsync", sagaDataType!, sagaId);
                    var contextInstance = Activator.CreateInstance(contextType, sagaId, loadedSagaData, eventBus, sagaStore, sagaIdGenerator);
                    var initializeMethod = handlerType?.GetMethod("Initialize");
                    initializeMethod?.Invoke(handler, [contextInstance]);
                }
                // Reactive Compensation
                else if (handlerType.IsSubclassOfRawGenericBase(typeof(ReactiveSagaHandler<>)) ||
                         handlerType.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>)))
                {
                    var genericArgs = handlerType?.BaseType?.GetGenericArguments();
                    var msgType = genericArgs?[0];

                    var contextType = typeof(SagaContext<>).MakeGenericType(msgType!);
                    var contextInstance = Activator.CreateInstance(contextType, sagaId, eventBus, sagaStore, sagaIdGenerator);
                    var initializeMethod = handlerType?.GetMethod("Initialize");
                    initializeMethod?.Invoke(handler, [contextInstance]);
                }

                // Call the compensation method
                var compensationInterface = typeof(ISagaCompensationHandler<>).MakeGenericType(messageType);
                var method = compensationInterface.GetMethod("CompensateAsync");
                if (method != null)
                {

                    try
                    {
#if UNIT_TESTING
                        await LogStep();
#endif
                        await (Task)method.Invoke(handler, [message])!;
#if UNIT_TESTING
                        await LogStep();
#endif
                        
                        // Check step status 
                        var status = await sagaStore.GetStepStatusAsync(sagaId, message.GetType());
                        if (status == StepStatus.CompensationFailed)
                        {
                            // Break the compensation chain if compensation failed
                            Console.WriteLine("⚡ Compensation chain stopped due to failure.");
                            break; // or continue; to skip to the next handler
                        }
                    }
                    catch (Exception ex)
                    {
                        // Break the compensation chain if an exception occurs (double safety)
                        Console.WriteLine("⚡ Compensation chain stopped due to exception.");
                        break;
                    }
                }
            }
        }

#if UNIT_TESTING
        if (compensationHandlers.Count == 0 && startHandlers.Count == 0)
            await LogStep();
#endif

        async Task LogStep()
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

                await sagaStore.LogStepAsync(sagaId, message.__TestStepType, message.__TestStepStatus.Value, message);
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

        if (IsResponse(messageType) &&
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
            var handlerType = handler?.GetType();
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
                var genericArgs = handlerType?.BaseType?.GetGenericArguments();
                var msgType = genericArgs?[0];
                var sagaDataType = genericArgs?[2];
                
                // Create SagaContext with loaded SagaData: SagaContext<msgType, sagaDataType>
                var contextType = typeof(SagaContext<,>).MakeGenericType(msgType!, sagaDataType!);
                var loadedSagaData = await sagaStore.InvokeGenericTaskResultAsync("LoadSagaDataAsync", sagaDataType!, sagaId);
                var contextInstance = Activator.CreateInstance(contextType, sagaId, loadedSagaData, eventBus, sagaStore,
                    sagaIdGenerator);
                var initializeMethod = handlerType?.GetMethod("Initialize");
                initializeMethod?.Invoke(handler, [contextInstance]);

                await HandleSagaAsync(handler);
                continue;
            }

            // Reactive (no SagaData)
            if (handlerType.IsSubclassOfRawGenericBase(typeof(ReactiveSagaHandler<>)) ||
                handlerType.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>)))
            {
                var genericArgs = handlerType?.BaseType?.GetGenericArguments();
                var msgType = genericArgs?[0];

                var contextType = typeof(SagaContext<>).MakeGenericType(msgType!);
                var contextInstance =
                    Activator.CreateInstance(contextType, sagaId, eventBus, sagaStore, sagaIdGenerator);
                var initializeMethod = handlerType?.GetMethod("Initialize");
                initializeMethod?.Invoke(handler, [contextInstance]);

                await HandleSagaAsync(handler);
                continue;
            }
        }

        async Task HandleSagaAsync(object? handler)
        {
            if (handler == null) return;

            var handlerType = handler.GetType();

            // if the handler is a StartCoordinatedSagaHandler or StartReactiveSagaHandler, call HandleStartAsync
            if (handlerType.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>))
                || handlerType.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,,>)))
            {
                // Call HandleStartAsync
                try
                {
                    dynamic dynamicHandler = handler;
                    
                    var sagaId = GetSagaId(message);
#if UNIT_TESTING
                    // Log step before handler execution
                    if (message is { __TestStepStatus: not null, __TestStepType: not null })
                    {
                        await sagaStore.LogStepAsync(sagaId, message.__TestStepType, message.__TestStepStatus.Value, message);
                    }
#endif
                    
                    await dynamicHandler.HandleStartAsync((dynamic)message);
                    
#if UNIT_TESTING
                    // Log step before handler execution
                    if (message is { __TestStepStatus: not null, __TestStepType: not null })
                    {
                        await sagaStore.LogStepAsync(sagaId, message.__TestStepType, message.__TestStepStatus.Value, message);
                    }
#endif
                    
                    await ValidateSagaStepCompletionAsync(message, sagaId);
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
                    
#if UNIT_TESTING
                    // Log step before handler execution
                    if (message is { __TestStepStatus: not null, __TestStepType: not null })
                    {
                        await sagaStore.LogStepAsync(sagaId, message.__TestStepType, message.__TestStepStatus.Value, message);
                    }
#endif
                    
                    await dynamicHandler.HandleAsync((dynamic)message);
                    
#if UNIT_TESTING
                    // Log step before handler execution
                    if (message is { __TestStepStatus: not null, __TestStepType: not null })
                    {
                        await sagaStore.LogStepAsync(sagaId, message.__TestStepType, message.__TestStepStatus.Value, message);
                    }
#endif
                    
                    await ValidateSagaStepCompletionAsync(message, sagaId);
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

    private async Task ValidateSagaStepCompletionAsync(IMessage message, Guid sagaId)
    {
        var stepTypeToCheck = message.GetType();
        var alreadyMarked = await sagaStore.IsStepCompletedAsync(sagaId, stepTypeToCheck);
        if (!alreadyMarked)
        {
            Console.WriteLine($"Step for {stepTypeToCheck.Name} was not marked as completed, failed, or compensated.");
            // If you want to throw an exception, uncomment the line below:
            // throw new InvalidOperationException($"Step {stepTypeToCheck.Name} was not marked as completed/failed/compensated.");
        }
    }

    private static bool IsResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IResponse<>));

    private static bool IsSuccessResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISuccessResponse<>));

    private static bool IsFailResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IFailResponse<>));

    private static bool IsCommand(Type type) => typeof(ICommand).IsAssignableFrom(type);
    private static bool IsEvent(Type type) => typeof(IEvent).IsAssignableFrom(type);
}