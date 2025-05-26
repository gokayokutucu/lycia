using System.Reflection;
using Lycia.Infrastructure.Abstractions;
using Lycia.Messaging;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;
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
        var startHandlers = serviceProvider.GetServices(startHandlerType); // Use injected
        if (startHandlers.Any())
        {
            Console.WriteLine($"[Dispatch] Dispatching {messageType.Name} to start handler: {startHandlerType.Name}");
            await InvokeHandlerAsync(startHandlerType, messageType, message);
            return;
        }

        var compensationHandlerType = typeof(ISagaCompensationHandler<>).MakeGenericType(messageType);
        var compensationHandlers = serviceProvider.GetServices(compensationHandlerType); // Fixed: Use _serviceProvider
        var enumerable = compensationHandlers.ToList();
        if (enumerable.Count != 0)
        {
            foreach (var handler in enumerable)
            {
                var compensationInterface = typeof(ISagaCompensationHandler<>).MakeGenericType(messageType);
                if (compensationInterface.IsInstanceOfType(handler))
                {
                    var method = compensationInterface.GetMethod("CompensateAsync");
                    if (method != null)
                    {
                        await (Task)method.Invoke(handler, [message])!;
                    }
                }
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
            // Skip event/command fallback if this is a response type
            return;
        }

        if (IsSuccessResponse(messageType))
        {
            var handlerType = typeof(ISuccessResponseHandler<>).MakeGenericType(messageType);
            Console.WriteLine($"[Dispatch] Dispatching {messageType.Name} to {handlerType.Name}");
            await InvokeHandlerAsync(handlerType, typeof(TResponse), message, null, typeof(TMessage));
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
            await InvokeHandlerAsync(handlerType, message.GetType(), message, fail, typeof(TMessage));
        }
        else
        {
            await DispatchByMessageTypeAsync(message);
        }
    }

    private async Task InvokeHandlerAsync(
        Type handlerType,
        Type messageParameterType,
        IMessage message,
        FailResponse? fail = null,
        Type? stepType = null)
    {
        Console.WriteLine(
            $"[InvokeHandlerAsync] Resolving handler type: {handlerType.Name} for message type: {messageParameterType.Name}");
        var handlers = serviceProvider.GetServices(handlerType); // Use injected
        var isSingleHandler = IsSingleHandlerExpected(handlerType);

        foreach (var handler in handlers)
        {
            // Try to find double-generic (Orchestration) context first
            var coordinatedHandlerInterface = handler?.GetType()
                .GetInterfaces()
                .FirstOrDefault(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition().Name == "ISagaHandlerWithContext`2"
                );

            var reactiveHandlerInterface = handler?.GetType()
                .GetInterfaces()
                .FirstOrDefault(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == typeof(ISagaHandlerWithContext<>)
                );

            Guid sagaId = Guid.Empty;

            // Orchestration / Coordinated handler (with saga data)
            if (coordinatedHandlerInterface != null)
            {
                var sagaIdProperty = message.GetType().GetProperty("SagaId");
                bool isStartHandler = handlerType.IsGenericType &&
                                      handlerType.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>);

                sagaId =
                    sagaIdProperty != null && sagaIdProperty.GetValue(message) is Guid value && value != Guid.Empty
                        ? value
                        : isStartHandler
                            ? sagaIdGenerator.Generate() // Use injected
                            : throw new InvalidOperationException("Missing SagaId on a non-starting message.");

                // ISagaHandlerWithContext<TInitialMessage, TSagaData>
                var contextMessageType = coordinatedHandlerInterface.GetGenericArguments()[0];
                var sagaDataType = coordinatedHandlerInterface.GetGenericArguments()[1];
                
                var loadDataMethod = typeof(ISagaStore).GetMethod("LoadContextAsync")!.MakeGenericMethod(sagaDataType);
                var loadTask = (Task)loadDataMethod.Invoke(sagaStore, [sagaId])!; // Use injected
                await loadTask.ConfigureAwait(false);
                var resultProperty = loadTask.GetType().GetProperty("Result");
                var loadedSagaData = resultProperty!.GetValue(loadTask)!; // Fixed: use loadTask
                // Create the actual SagaContext for ISagaHandlerWithContext<TMessage, TSagaData>
                var sagaContextType = typeof(SagaContext<,>).MakeGenericType(contextMessageType, sagaDataType);
                var context = Activator.CreateInstance(sagaContextType, sagaId, loadedSagaData, eventBus, sagaStore, sagaIdGenerator);
                var initializeMethod = coordinatedHandlerInterface.GetMethod("Initialize");
                initializeMethod?.Invoke(handler, [context]);
            }
            // Choreography / Reactive handler (context only)
            else if (reactiveHandlerInterface != null)
            {
                var sagaIdProperty = message.GetType().GetProperty("SagaId");
                bool isStartHandler = handlerType.IsGenericType &&
                                      handlerType.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>);

                sagaId =
                    sagaIdProperty != null && sagaIdProperty.GetValue(message) is Guid value && value != Guid.Empty
                        ? value
                        : isStartHandler
                            ? sagaIdGenerator.Generate() // Use injected
                            : throw new InvalidOperationException("Missing SagaId on a non-starting message.");

                var reactiveContextMessageType = reactiveHandlerInterface.GetGenericArguments()[0];
                var contextType = typeof(SagaContext<>).MakeGenericType(reactiveContextMessageType);
                // Constructor: SagaContext(Guid sagaId, IEventBus eventBus, ISagaStore sagaStore, ISagaIdGenerator sagaIdGenerator)
                var context = Activator.CreateInstance(contextType, sagaId, eventBus, sagaStore, sagaIdGenerator); // Use injected

                var initializeMethod = reactiveHandlerInterface.GetMethod("Initialize");
                initializeMethod?.Invoke(handler, [context]);
            }

            // Backward compatibility: genericless
            else if (handler is ISagaHandlerWithContext<IMessage> genericLess)
            {
                var sagaIdProperty = message.GetType().GetProperty("SagaId");
                bool isStartHandler = handlerType.IsGenericType &&
                                      handlerType.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>);

                sagaId =
                    sagaIdProperty != null && sagaIdProperty.GetValue(message) is Guid val && val != Guid.Empty
                        ? val
                        : isStartHandler
                            ? sagaIdGenerator.Generate() // Use injected
                            : throw new InvalidOperationException("Missing SagaId on a non-starting message.");

                var context = new SagaContext<IMessage>(sagaId, eventBus, sagaStore, sagaIdGenerator); // Use injected
                genericLess.Initialize(context);
            }

            MethodInfo? method;
            object[] parameters;

            if (fail is not null)
            {
                method = handler?.GetType()
                    .GetMethods()
                    .FirstOrDefault(m =>
                    {
                        var ps = m.GetParameters();
                        return m.Name == "HandleFailResponseAsync"
                               && ps.Length == 2
                               && ps[1].ParameterType == typeof(FailResponse);
                    });

                if (method == null)
                    throw new InvalidOperationException(
                        $"Handler of type {handler?.GetType().Name} does not implement HandleFailResponseAsync method.");

                var expectedMessageType = method.GetParameters()[0].ParameterType;

                if (!expectedMessageType.IsInstanceOfType(message))
                {
                    throw new InvalidOperationException(
                        $"Cannot pass message of type '{message.GetType().Name}' to handler expecting '{expectedMessageType.Name}'."
                    );
                }

                parameters =
                [
                    message,
                    fail
                ];
            }
            else
            {
                // Select method based on the handler type
                if (handlerType.IsGenericType &&
                    handlerType.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>))
                {
                    method = handler?.GetType().GetMethod("HandleSuccessResponseAsync");
                }
                else if (handlerType.IsGenericType &&
                         handlerType.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>))
                {
                    method = handler?.GetType().GetMethod("HandleFailResponseAsync");
                }
                else if (handlerType.IsGenericType &&
                         handlerType.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>))
                {
                    method = handler?.GetType().GetMethod("HandleStartAsync");
                }
                else
                {
                    // Fallback to a generic method but HandleAsync not implemented yet
                    method = handler?.GetType().GetMethod("HandleAsync");
                }

                parameters = [message];
            }

            Console.WriteLine(
                $"[InvokeHandlerAsync] Invoking method: {method?.Name} on handler: {handler?.GetType().Name}");
            if (method is not null)
            {
                await (Task)method.Invoke(handler, parameters)!;
                // After method.Invoke(...)
                var stepTypeToCheck = stepType ?? messageParameterType;
                var alreadyMarked = await sagaStore.IsStepCompletedAsync(sagaId, stepTypeToCheck); // Use injected
                if (!alreadyMarked)
                {
                    Console.WriteLine($"Step for {stepTypeToCheck.Name} was not marked as completed, failed, or compensated.");
                }
            }
            
            if (isSingleHandler)
                break;
        }
    }

    private static bool IsSuccessResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISuccessResponse<>));

    private static bool IsFailResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IFailResponse<>));

    private static bool IsCommand(Type type) => typeof(ICommand).IsAssignableFrom(type);
    private static bool IsEvent(Type type) => typeof(IEvent).IsAssignableFrom(type);

    private static bool IsSingleHandlerExpected(Type handlerType)
    {
        return handlerType.IsGenericType &&
               (
                   handlerType.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>) ||
                   handlerType.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>) ||
                   handlerType.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)
               );
    }
}