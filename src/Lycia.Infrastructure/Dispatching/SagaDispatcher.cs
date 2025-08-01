using Lycia.Messaging;
using Lycia.Saga;
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
            Console.WriteLine($"[Dispatch] Dispatching {messageType.Name} to {handlerType.Name}");
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
            Console.WriteLine($"[Dispatch] Dispatching {messageType.Name} to {handlerType.Name}");
            await InvokeHandlerAsync(serviceProvider.GetServices(handlerType), message, sagaId, fail,
                cancellationToken);
        }
        else
        {
            await DispatchByMessageTypeAsync(message, handlerType, sagaId, cancellationToken);
        }
    }

    protected virtual async Task InvokeHandlerAsync(
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
        else if (sagaId is null && sagaIdProp is null)
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
                compensationCoordinator);
            var initializeMethod = handlerType.GetMethod("Initialize");
            initializeMethod?.Invoke(handler, [contextInstance]);

            await HandleSagaAsync(message, handler, handlerType);
            return;
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
                    compensationCoordinator);
            var initializeMethod = handlerType.GetMethod("Initialize");
            initializeMethod?.Invoke(handler, [contextInstance]);

            await HandleSagaAsync(message, handler, handlerType);
        }
    }
    
    private async Task HandleSagaAsync(IMessage message, object? handler, Type handlerType)
    {
        if (handler == null) return;

        // Call HandleStartAsync
        try
        {
            var sagaId = GetSagaId(message);

            var delegateMethod =
                HandlerDelegateHelper.GetHandlerDelegate(handlerType, "HandleAsyncInternal", message.GetType());
            await delegateMethod(handler, message);

            await ValidateSagaStepCompletionAsync(message, handlerType, sagaId);
        }
        catch (Exception ex)
        {
            // Optionally log or throw a descriptive error
            throw new InvalidOperationException($"Failed to invoke HandleAsync dynamically: {ex.Message}", ex);
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