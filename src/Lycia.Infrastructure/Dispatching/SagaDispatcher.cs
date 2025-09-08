// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Lycia.Saga.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Infrastructure.Dispatching;

/// <summary>
/// Responsible for resolving and invoking saga-related handlers for incoming messages.
/// </summary>
public class SagaDispatcher(
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    IServiceProvider serviceProvider)
    : ISagaDispatcher
{
    private async Task DispatchByMessageTypeAsync<TMessage>(TMessage message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken) where TMessage : IMessage
    {
        if (handlerType == null)
        {
            // log null handlerType
            return;
        }

        var handler = serviceProvider.GetRequiredService(handlerType);
        await InvokeHandlerAsync(handler, message, sagaId, cancellationToken: cancellationToken);
    }

    public async Task DispatchAsync<TMessage>(TMessage message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken) where TMessage : IMessage
    {
             await DispatchByMessageTypeAsync(message, handlerType, sagaId, cancellationToken);
    }

    public async Task DispatchAsync<TMessage, TResponse>(TResponse message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken)
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
            Console.WriteLine($"[Dispatch] Dispatching {messageType.Name} to {handlerType!.Name}");
            await InvokeHandlerAsync(serviceProvider.GetServices(handlerType), message,
                cancellationToken: cancellationToken);
        }
        else if (IsFailResponse(messageType))
        {
            var fail = new FailResponse
            {
                Reason = "An error occurred while handling the message.",
                ExceptionType = message.GetType().Name,
                OccurredAt = DateTime.UtcNow
            };
            Console.WriteLine($"[Dispatch] Dispatching {messageType.Name} to {handlerType?.Name}");
            await InvokeHandlerAsync(serviceProvider.GetServices(handlerType!), message, sagaId, fail,
                cancellationToken);
        }
        else
        {
            await DispatchByMessageTypeAsync(message, handlerType, sagaId, cancellationToken);
        }
    }

    private async Task InvokeHandlerAsync(
        object? handler,
        IMessage message,
        Guid? sagaId = null,
        FailResponse? fail = null, CancellationToken cancellationToken = default)
    {
        if (serviceProvider.GetService(typeof(IEventBus)) is not IEventBus eventBus)
            throw new InvalidOperationException("IEventBus not resolved.");

        if (serviceProvider.GetService(typeof(ISagaCompensationCoordinator)) is not ISagaCompensationCoordinator
            compensationCoordinator)
            throw new InvalidOperationException("ISagaCompensationCoordinator not resolved.");


        // SagaId resolution logic
        var messageType = message.GetType();
        var sagaIdProp = messageType.GetProperty("SagaId");

        // Only ISagaStartHandler gets a new SagaId if needed
        var handlerType = handler!.GetType();
        var isStartHandler = handlerType.IsSubclassOfRawGeneric(typeof(ISagaStartHandler<>)) ||
                             handlerType.IsSubclassOfRawGeneric(typeof(ISagaStartHandler<,>));

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
        else if (sagaId is null && sagaIdProp is null)
        {
            // Not a start handler and SagaId missing: throw!
            throw new InvalidOperationException("Missing SagaId on a non-starting message.");
        }
        
        if (!IsSupportedSagaHandler(handlerType)) return;
        
        await SagaContextFactory.InitializeForHandlerAsync(
            handler,
            sagaId!.Value,
            message,
            eventBus,
            sagaStore,
            sagaIdGenerator,
            compensationCoordinator, 
            serviceProvider, 
            cancellationToken);
        
        await HandleSagaAsync(message, handler, handlerType, cancellationToken);
    }

    private async Task HandleSagaAsync(IMessage message, object? handler, Type handlerType, CancellationToken cancellationToken)
    {
        if (handler == null) return;

        // Call HandleStartAsync
        try
        {
            var sagaId = GetSagaId(message);

            var msgType = message.GetType();
            var methodName = FindMethodName(msgType);

            var delegateMethod = 
                HandlerDelegateHelper.GetHandlerDelegate(handlerType, methodName, msgType);
            await delegateMethod(handler, message, cancellationToken);

            await ValidateSagaStepCompletionAsync(message, handlerType, sagaId);
        }
        catch (Exception ex)
        {
            // Optionally log or throw a descriptive error
            throw new InvalidOperationException($"Failed to invoke HandleAsync dynamically: {ex.Message}", ex);
        }
    }

    private static string FindMethodName(Type msgType)
    {
        if (typeof(FailedEventBase).IsAssignableFrom(msgType))
        {
            // Choreography: failed events should invoke ISagaCompensationHandler<TFailed>.CompensateAsync
            return "CompensateAsync";
        }
        
        if (msgType.IsSuccessResponse())
        {
           return "HandleSuccessResponseAsync";
        }

        return "HandleAsyncInternal";
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
    
    private static bool IsSupportedSagaHandler(Type t) =>
        t.IsSubclassOfRawGenericBase(typeof(CoordinatedSagaHandler<,>)) ||
        t.IsSubclassOfRawGenericBase(typeof(CoordinatedResponsiveSagaHandler<,,>)) ||
        t.IsSubclassOfRawGenericBase(typeof(StartCoordinatedResponsiveSagaHandler<,,>)) ||
        t.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,>)) ||
        t.IsSubclassOfRawGenericBase(typeof(ReactiveSagaHandler<>)) ||
        t.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>));

    private static bool IsSuccessResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISuccessResponse<>));

    private static bool IsFailResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IFailResponse<>));

    private static bool IsCommand(Type type) => typeof(ICommand).IsAssignableFrom(type);
    private static bool IsEvent(Type type) => typeof(IEvent).IsAssignableFrom(type);
}