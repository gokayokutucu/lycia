// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Reflection;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Configurations;
using Lycia.Saga.Handlers.Abstractions;
using Microsoft.Extensions.Options;

namespace Lycia.Saga.Handlers;

public abstract class StartCoordinatedResponsiveSagaHandler<TMessage, TResponse, TSagaData> :
    ISagaStartHandler<TMessage, TSagaData>,
    IResponseSagaHandler<TResponse>
    where TMessage : IMessage
    where TResponse : IResponse<TMessage>
    where TSagaData : SagaData
{
    protected ISagaContext<IMessage, TSagaData> Context { get; private set; } = null!;
    
    public void Initialize(ISagaContext<IMessage, TSagaData> context, IOptions<SagaOptions> sagaOptions)
    {
        Context = context;
    }

    public abstract Task HandleStartAsync(TMessage message, CancellationToken cancellationToken = default);

    protected async Task HandleAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await HandleStartAsync(message, cancellationToken); // Actual business logic
        }
        catch (OperationCanceledException ex)
        {
            await Context.MarkAsCancelled<TMessage>(ex);
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<TMessage>(ex, cancellationToken);
        }
    }

    protected async Task CompensateAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CompensateStartAsync(message, cancellationToken); // Actual business logic

            // After custom compensation logic, invoke the fail handler for the failed step if needed
            await InvokeFailedStepHandlerAsync(this, Context.Data, Context.SagaStore, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            await Context.MarkAsCancelled<TMessage>(ex);
        }
        catch (Exception ex)
        {
            await Context.MarkAsCompensationFailed<TMessage>(ex);
        }
    }

    public virtual Task CompensateStartAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task HandleSuccessResponseAsync(TResponse response, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task HandleFailResponseAsync(TResponse response, FailResponse fail, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
    
    protected Task MarkAsComplete(CancellationToken cancellationToken = default) => Context.MarkAsComplete<TMessage>();
    protected Task MarkAsFailed(CancellationToken cancellationToken = default) => Context.MarkAsFailed<TMessage>(cancellationToken);
    protected Task MarkAsCompensationFailed(CancellationToken cancellationToken = default) => Context.MarkAsCompensationFailed<TMessage>();
    protected Task<bool> IsAlreadyCompleted(CancellationToken cancellationToken = default) => Context.IsAlreadyCompleted<TMessage>();

    /// <summary>
    /// Handles the invocation of a failure handler for a specific failed step within a saga process.
    /// </summary>
    /// <param name="handlerInstance">The instance of the handler managing the saga.</param>
    /// <param name="sagaData">The shared saga data containing details of the current state and failed step.</param>
    /// <param name="sagaStore">The saga store used to manage persistence of saga state.</param>
    /// <param name="cancellationToken">The cancellation token to observe during asynchronous operations, if provided.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if there are issues identifying or invoking the failure handler for the failed step.</exception>
    private static async Task InvokeFailedStepHandlerAsync(
        object handlerInstance,
        SagaData sagaData,
        ISagaStore sagaStore,
        CancellationToken cancellationToken = default)
    {
        // Determine which command type failed (e.g., ProcessPaymentCommand)
        var failedStepType = sagaData.FailedStepType;
        if (failedStepType == null) return;

        var handlerType = handlerInstance.GetType();

        // Iterate over IResponseSagaHandler<TResponse> implemented by the central handler
        var responseHandlerInterfaces = handlerType
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IResponseSagaHandler<>));

        foreach (var iface in responseHandlerInterfaces)
        {
            var responseType = iface.GetGenericArguments()[0]; // e.g., PaymentSucceededResponse

            // Find IResponse<TCommand> on the response type to learn which command it responds to
            var respIface = responseType
                .GetInterfaces()
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IResponse<>));

            var respondedCommandType = respIface?.GetGenericArguments()[0]; // e.g., ProcessPaymentCommand

            // Allow assignability (in case of base/derived command types)
            if (respondedCommandType is null || !respondedCommandType.IsAssignableFrom(failedStepType))
                continue;

            // Try to get the public handler first
            var method =
                handlerType.GetMethod("HandleFailResponseAsync", [responseType, typeof(FailResponse), typeof(CancellationToken)
                ])
                ?? handlerType.GetMethod("HandleFailResponseAsync", [responseType, typeof(FailResponse)])
                ?? TryToFindExplicitFailResponseHandler(handlerType, iface, responseType, expectCancellationToken: true)
                ?? TryToFindExplicitFailResponseHandler(handlerType, iface, responseType, expectCancellationToken: false);

            if (method == null)
                continue;

            // Try to load the real response from the store; if not present, synthesize one
            var responseInstance = await sagaStore.LoadSagaStepMessageAsync(sagaData.SagaId, responseType)
                                   ?? await CreateSyntheticResponseAsync(sagaStore, sagaData.SagaId, failedStepType, responseType);
            if (responseInstance is null)
                continue;

            var fail = new FailResponse
            {
                Reason = "Saga compensation chain completed.",
                OccurredAt = sagaData.FailedAt ?? DateTime.UtcNow
            };

            var pars = method.GetParameters();
            object?[] args = pars.Length == 3
                ? [responseInstance, fail, cancellationToken]
                : [responseInstance, fail];

            var taskObj = method.Invoke(handlerInstance, args);
            if (taskObj is Task task)
                await task;
            else
                throw new InvalidOperationException("HandleFailResponseAsync must return a Task.");

            break;
        }
    }

    private static async Task<object?> CreateSyntheticResponseAsync(ISagaStore sagaStore, Guid sagaId, Type failedCommandType, Type responseType)
    {
        // If the failed command instance exists, use it to populate a minimal response
        var failedCommand = await sagaStore.LoadSagaStepMessageAsync(sagaId, failedCommandType);
        var resp = Activator.CreateInstance(responseType);
        if (resp is null)
            return null;

        // Best-effort wiring of correlation fields
        // IMessage-common fields: SagaId, ParentMessageId, MessageId
        TrySetProperty(resp, "SagaId", GetProperty(failedCommand, "SagaId", true) ?? sagaId, true);
        TrySetProperty(resp, "ParentMessageId", GetProperty(failedCommand, "MessageId", true), true);
        TryCopyProperty(resp, failedCommand, "CorrelationId", true);
        TrySetProperty(resp, "MessageId", Guid.NewGuid(), true); 
        TrySetProperty(resp, "Timestamp", DateTime.UtcNow, true);
        TryCopyProperty(resp, failedCommand, "ApplicationId", true);

        return resp;
    }

    private static void TryCopyProperty(object? target, object? source, string propertyName, bool includeNonPublic = false)
    {
        if (target is null || source is null) return;
        var sv = GetProperty(source, propertyName, includeNonPublic);
        if (sv is null) return;
        TrySetProperty(target, propertyName, sv, includeNonPublic);
    }

    private static object? GetProperty(object? obj, string name, bool includeNonPublic = false)
    {
        if (obj is null) return null;
        var flags = BindingFlags.Instance | BindingFlags.Public;
        if (includeNonPublic)
            flags |= BindingFlags.NonPublic;
        return obj.GetType().GetProperty(name, flags)?.GetValue(obj);
    }

    private static void TrySetProperty(object obj, string name, object? value, bool includeNonPublic = false)
    {
        if (value is null) return;
        var flags = BindingFlags.Instance | BindingFlags.Public;
        if (includeNonPublic)
            flags |= BindingFlags.NonPublic;
        var p = obj.GetType().GetProperty(name, flags);
        if (p is null || !p.CanWrite) return;
        try { p.SetValue(obj, value); } catch { /* ignore best-effort */ }
    }

    private static MethodInfo? TryToFindExplicitFailResponseHandler(Type handlerType, Type iface, Type responseType, bool expectCancellationToken)
    {
        // Look for an explicit interface implementation of IResponseSagaHandler<T>.HandleFailResponseAsync
        var map = handlerType.GetInterfaceMap(iface);
        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            var im = map.InterfaceMethods[i];
            if (!im.Name.EndsWith(".HandleFailResponseAsync", StringComparison.Ordinal)) continue;
            var tm = map.TargetMethods[i];
            var pars = tm.GetParameters();

            if (expectCancellationToken)
            {
                if (pars.Length == 3 &&
                    pars[0].ParameterType.IsAssignableFrom(responseType) &&
                    pars[1].ParameterType == typeof(FailResponse) &&
                    pars[2].ParameterType == typeof(CancellationToken))
                {
                    return tm;
                }
            }
            else
            {
                if (pars.Length == 2 &&
                    pars[0].ParameterType.IsAssignableFrom(responseType) &&
                    pars[1].ParameterType == typeof(FailResponse))
                {
                    return tm;
                }
            }
        }

        return null;
    }
}