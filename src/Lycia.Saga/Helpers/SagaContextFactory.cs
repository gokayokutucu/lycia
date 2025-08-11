using System.Reflection;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;

namespace Lycia.Saga.Helpers;

/// <summary>
/// Builds the correct SagaContext instance for a handler by inspecting the handler's Initialize(...) signature.
/// This mirrors the logic you already use in the dispatcher and coordinator, so both can share one implementation.
/// </summary>
public static class SagaContextFactory
{
    /// <summary>
    /// Creates the proper SagaContext instance (SagaContext&lt;T&gt; or SagaContext&lt;T, TSagaData&gt;)
    /// based on the handler's Initialize(...) parameter type. Then invokes Initialize(handler, context).
    /// </summary>
    public static async Task<(MethodInfo initializeMethod, object contextInstance)> InitializeForHandlerAsync(
        object handler,
        Guid sagaId,
        object currentMessage,
        IEventBus eventBus,
        ISagaStore sagaStore,
        ISagaIdGenerator sagaIdGenerator,
        ISagaCompensationCoordinator compensationCoordinator)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        var handlerType = handler.GetType();
        var initializeMethod = handlerType.GetMethod("Initialize")
                              ?? throw new InvalidOperationException($"Initialize method not found on {handlerType.Name}");

        // We rely on the exact Initialize(...) parameter type: ISagaContext<T> or ISagaContext<T,TSagaData>
        var initParamType = initializeMethod.GetParameters().FirstOrDefault()?.ParameterType
                            ?? throw new InvalidOperationException("Initialize parameter type not found.");

        object contextInstance;

        if (initParamType.IsGenericType && initParamType.GetGenericTypeDefinition() == typeof(ISagaContext<,>))
        {
            // ex: ISagaContext<IMessage, TSagaData>
            var args = initParamType.GetGenericArguments(); // [TInitialMessageParam, TSagaDataParam]
            var initMsgArg  = args[0];                      // often IMessage
            var initDataArg = args[1];

            var loadedSagaData =
                await sagaStore.InvokeGenericTaskResultAsync("LoadSagaDataAsync", initDataArg, sagaId);

            var contextType = typeof(SagaContext<,>).MakeGenericType(initMsgArg, initDataArg);
            contextInstance = Activator.CreateInstance(
                contextType,
                sagaId,
                currentMessage,            // important: pass the actual current step (could be a Response)
                handlerType,
                loadedSagaData,
                eventBus,
                sagaStore,
                sagaIdGenerator,
                compensationCoordinator
            )!;
        }
        else if (initParamType.IsGenericType && initParamType.GetGenericTypeDefinition() == typeof(ISagaContext<>))
        {
            // ex: ISagaContext<IMessage>
            var initMsgArg = initParamType.GetGenericArguments()[0];
            var contextType = typeof(SagaContext<>).MakeGenericType(initMsgArg);

            contextInstance = Activator.CreateInstance(
                contextType,
                sagaId,
                currentMessage,            // same: use the message we're dispatching right now
                handlerType,
                eventBus,
                sagaStore,
                sagaIdGenerator,
                compensationCoordinator
            )!;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported Initialize parameter type: {initParamType}");
        }

        // Actually call Initialize(handler, context)
        initializeMethod.Invoke(handler, [contextInstance]);

        return (initializeMethod, contextInstance);
    }
}