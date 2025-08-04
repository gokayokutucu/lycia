using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers.Abstractions;

namespace Lycia.Saga.Handlers;

public abstract class StartCoordinatedResponsiveSagaHandler<TMessage, TResponse, TSagaData> :
    ISagaStartHandler<TMessage, TSagaData>,
    IResponseSagaHandler<TResponse>
    where TMessage : IMessage
    where TResponse : IResponse<TMessage>
    where TSagaData : new()
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
}