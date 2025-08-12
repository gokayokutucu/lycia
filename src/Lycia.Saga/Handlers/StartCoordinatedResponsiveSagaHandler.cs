using System.Reflection;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers.Abstractions;

namespace Lycia.Saga.Handlers;

public abstract class StartCoordinatedResponsiveSagaHandler<TMessage, TResponse, TSagaData> :
    ISagaStartHandler<TMessage, TSagaData>,
    IResponseSagaHandler<TResponse>
    where TMessage : IMessage
    where TResponse : IResponse<TMessage>
    where TSagaData : SagaData
{
    protected ISagaContext<IMessage, TSagaData> Context { get; private set; } = null!;
    
    public void Initialize(ISagaContext<IMessage, TSagaData> context)
    {
        Context = context;
    }

    public abstract Task HandleStartAsync(TMessage message);

    protected async Task HandleAsyncInternal(TMessage message)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await HandleStartAsync(message); // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsFailed<TMessage>();
        }
    }

    protected async Task CompensateAsyncInternal(TMessage message)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await CompensateStartAsync(message); // Actual business logic

            // After custom compensation logic, invoke the fail handler for the failed step if needed
            await InvokeFailedStepHandlerAsync(this, Context.Data, Context.SagaStore);
        }
        catch (Exception)
        {
            await Context.MarkAsCompensationFailed<TMessage>();
        }
    }

    public virtual Task CompensateStartAsync(TMessage message)
    {
        return Task.CompletedTask;
    }

    public virtual Task HandleSuccessResponseAsync(TResponse response)
    {
        return Task.CompletedTask;
    }

    public virtual Task HandleFailResponseAsync(TResponse response, FailResponse fail)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Finds and invokes the HandleFailResponseAsync method for the failed response type on the provided handler.
    /// </summary>
    /// <param name="handlerInstance">The saga handler instance.</param>
    /// <param name="sagaData">The saga data containing failed step information.</param>
    /// <param name="sagaStore">The saga store for loading the response instance.</param>
    /// <returns>Awaitable task.</returns>
    private static async Task InvokeFailedStepHandlerAsync(
        object handlerInstance,
        SagaData sagaData,
        ISagaStore sagaStore)
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
                handlerType.GetMethod("HandleFailResponseAsync", [responseType, typeof(FailResponse)])
                ?? TryToFindExplicitFailResponseHandler(handlerType, iface, responseType);

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

            // Invoke and await the Task result safely
            var taskObj = method.Invoke(handlerInstance, [responseInstance, (object)fail]);
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

    private static MethodInfo? TryToFindExplicitFailResponseHandler(Type handlerType, Type iface, Type responseType)
    {
        // Look for an explicit interface implementation of IResponseSagaHandler<T>.HandleFailResponseAsync
        var map = handlerType.GetInterfaceMap(iface);
        for (int i = 0; i < map.InterfaceMethods.Length; i++)
        {
            var im = map.InterfaceMethods[i];
            if (!im.Name.EndsWith(".HandleFailResponseAsync", StringComparison.Ordinal)) continue;
            var tm = map.TargetMethods[i];
            var pars = tm.GetParameters();
            if (pars.Length == 2 &&
                pars[0].ParameterType.IsAssignableFrom(responseType) &&
                pars[1].ParameterType == typeof(FailResponse))
            {
                return tm;
            }
        }

        return null;
    }
}