// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using System.Reflection;
using Lycia.Common.Configurations;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Contexts;
using Lycia.Saga.Helpers;
using Microsoft.Extensions.Options;

namespace Lycia.Helpers;

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
    public static async Task<object?> InitializeForHandlerAsync(
        object handler,
        Guid sagaId,
        object currentMessage,
        IEventBus eventBus,
        ISagaStore sagaStore,
        ISagaIdGenerator sagaIdGenerator,
        ISagaCompensationCoordinator compensationCoordinator,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        var handlerType = handler.GetType();

        // Prefer the overload Initialize(ISagaContext<...>, IOptions&lt;SagaOptions&gt;) if available,
        // otherwise fall back to Initialize(ISagaContext&lt;...&gt;)
        var initializeMethod = handlerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == "Initialize")
            .OrderByDescending(m => m.GetParameters().Length) // prefer 2-arg overload
            .FirstOrDefault();

        if (initializeMethod is null)
        {
            // No Initialize method found on handler — nothing to do.
            return null;
        }

        // We rely on the exact Initialize(...) parameter type: ISagaContext<T> or ISagaContext<T,TSagaData>
        var initParamType = initializeMethod.GetParameters().FirstOrDefault()?.ParameterType
                            ?? throw new InvalidOperationException("Initialize parameter type not found.");

        object? contextInstance;

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
            );
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
            );
        }
        else
        {
            throw new InvalidOperationException($"Unsupported Initialize parameter type: {initParamType}");
        }

        if (contextInstance is null)
        {
            throw new InvalidOperationException(
                $"Failed to create saga context instance for handler {handlerType.FullName} with message {currentMessage.GetType().FullName}.");
        }

        // Actually call Initialize(handler, context[, options])
        var initParams = initializeMethod.GetParameters();

        InvokeHandlerInitialization(handler, serviceProvider, initParams, initializeMethod, contextInstance, handlerType);
        return contextInstance;
    }

    private static void InvokeHandlerInitialization(
        object handler,
        IServiceProvider serviceProvider,
        ParameterInfo[] initParams,
        MethodInfo initializeMethod,
        object contextInstance,
        Type handlerType)
    {
        if (initParams.Length == 1)
        {
            // Initialize(ISagaContext<...>)
            initializeMethod.Invoke(handler, [contextInstance]);
            return;
        }

        if (initParams.Length == 2 &&
            typeof(IOptions<SagaOptions>).IsAssignableFrom(initParams[1].ParameterType))
        {
            // Initialize(ISagaContext<...>, IOptions<SagaOptions>)
            var optionsObj = serviceProvider.GetService(typeof(IOptions<SagaOptions>))
                             ?? Options.Create(new SagaOptions());
            initializeMethod.Invoke(handler, [contextInstance, optionsObj]);
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported Initialize signature on {handlerType.FullName}. Expected either " +
            $"Initialize(ISagaContext<...>) or Initialize(ISagaContext<...>, IOptions<{nameof(SagaOptions)}>). " +
            $"Found: {initializeMethod}.");
    }
}