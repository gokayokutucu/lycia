// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Messaging;
using Lycia.Common.Enums;
using Lycia.Contexts;
using Lycia.Extensions;
using Lycia.Helpers;
using Lycia.Middleware;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Middlewares;
using Lycia.Saga.Exceptions;
using Lycia.Saga.Helpers;
using Lycia.Saga.Messaging;
using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lycia.Dispatching;

/// <summary>
/// Responsible for resolving and invoking saga-related handlers for incoming messages.
/// </summary>
public class SagaDispatcher(
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    IServiceProvider serviceProvider,
    ILogger<SagaDispatcher> logger)
    : ISagaDispatcher
{
    private async Task DispatchByMessageTypeAsync<TMessage>(TMessage message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken) where TMessage : IMessage
    {
        if (handlerType == null)
        {
            logger.LogWarning("No handler type resolved for message {MessageType}", typeof(TMessage).Name);
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
            logger?.LogInformation("Dispatching {Message} to {Handler}", messageType.Name, handlerType!.Name);
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
            logger?.LogInformation("Dispatching {Message} to {Handler}", messageType.Name, handlerType?.Name);
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
        
        var createdContext = await SagaContextFactory.InitializeForHandlerAsync(
            handler,
            sagaId!.Value,
            message,
            eventBus,
            sagaStore,
            sagaIdGenerator,
            compensationCoordinator, 
            serviceProvider, 
            cancellationToken);
        
        // Build middleware pipeline and execute handler through it
        var middlewares = serviceProvider.GetServices<ISagaMiddleware>();
        var orderedTypes = serviceProvider.GetService<IReadOnlyList<Type>>();
        var pipeline = new Middleware.SagaMiddlewarePipeline(middlewares, serviceProvider, orderedTypes);
        var ctx = new SagaContextInvocationContext
        {
            Message = message,
            SagaContext = createdContext as ISagaContext,
            HandlerType = handlerType,
            SagaId = sagaId,
            ApplicationId = message.ApplicationId,
            CancellationToken = cancellationToken
        };
        
        var sagaContextAccessor = serviceProvider.GetService<ISagaContextAccessor>();

        // Set current saga context in accessor if available
        var previous = sagaContextAccessor?.Current;
        try
        {
            if (sagaContextAccessor != null)
                sagaContextAccessor.Current = createdContext as ISagaContext;
            await pipeline.InvokeAsync(ctx, () => HandleSagaAsync(message, handler, handlerType, cancellationToken));
        }
        finally
        {
            if (sagaContextAccessor != null) sagaContextAccessor.Current = previous;
        }
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
            logger?.LogError(ex, "Failed to invoke saga handler dynamically: {Message}", ex.Message);
            throw new SagaDispatchException($"Failed to invoke HandleAsync dynamically: {ex.Message}", ex);
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

        // Use status to validate all terminal outcomes, not just "Completed"
        var status = await sagaStore.GetStepStatusAsync(
            sagaId,
            message.MessageId,
            stepTypeToCheck,
            handlerType);

        var isTerminal = status is StepStatus.Completed or StepStatus.Failed or StepStatus.Compensated;

        if (!isTerminal)
        {
            logger.LogWarning(
                "Step for {Step} has status {Status} - expected Completed/Failed/Compensated",
                stepTypeToCheck.Name,
                status);
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