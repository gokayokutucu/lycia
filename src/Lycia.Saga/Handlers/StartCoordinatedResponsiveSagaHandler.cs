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
    protected ISagaContext<TMessage, TSagaData> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<TMessage, TSagaData> context)
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
            await InvokeFailedStepHandlerAsync(Context.HandlerTypeOfCurrentStep, Context.Data, Context.SagaStore);
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
    /// <param name="handler">The saga handler instance.</param>
    /// <param name="sagaData">The saga data containing failed step information.</param>
    /// <param name="sagaStore">The saga store for loading the response instance.</param>
    /// <returns>Awaitable task.</returns>
    private static async Task InvokeFailedStepHandlerAsync(
        object handler,
        SagaData sagaData,
        ISagaStore sagaStore)
    {
        // 1. Get the failed step response type from SagaData
        var failedStepType = sagaData.FailedStepType;
        if (failedStepType == null)
            throw new InvalidOperationException("FailedStepType is not set in SagaData.");

        // 2. Find all IResponseSagaHandler<T> interfaces implemented by the handler
        var responseHandlerInterfaces = handler.GetType()
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IResponseSagaHandler<>));

        foreach (var iface in responseHandlerInterfaces)
        {
            var responseType = iface.GetGenericArguments()[0];
            if (responseType != failedStepType) continue;
            // 3. Find the HandleFailResponseAsync method
            var method = handler.GetType().GetMethod(
                "HandleFailResponseAsync",
                [responseType, typeof(FailResponse)]);

            if (method == null) continue;
            // 4. Load the failed response instance (from SagaStore)
            // You need to decide how to identify which message to load.
            // Here we assume SagaStore has a method like LoadSagaStepMessageAsync
            // that returns the correct response object for the failed step.
            var responseInstance = await sagaStore.LoadSagaStepMessageAsync(sagaData.SagaId, failedStepType);
            if (responseInstance == null)
                throw new InvalidOperationException(
                    $"No instance found in SagaStore for type {failedStepType.Name}");

            // Create or obtain FailResponse as needed (could also be saved in SagaData)
            var fail = new FailResponse
            {
                Reason = "Saga compensation chain completed.",
                ExceptionType = "Compensation",
                OccurredAt = sagaData.FailedAt ?? DateTime.UtcNow
            };

            // 5. Invoke the fail handler asynchronously
            var task = (Task)method.Invoke(handler, [responseInstance, fail])!;
            await task;
            break;
        }
    }
}