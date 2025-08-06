using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers.Abstractions;

namespace Lycia.Saga.Handlers;

public abstract class CoordinatedResponsiveSagaHandler<TMessage, TResponse, TSagaData> :
    ISagaHandler<TMessage, TSagaData>
    where TMessage : IMessage
    where TResponse : IResponse<TMessage>
    where TSagaData : SagaData
{
    protected ISagaContext<TMessage, TSagaData> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<TMessage, TSagaData> context)
    {
        Context = context;
    }

    protected async Task HandleAsyncInternal(TMessage message)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await HandleAsync(message);  // Actual business logic
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
            await CompensateAsync(message);  // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsCompensationFailed<TMessage>();
        }
    }

    public abstract Task HandleAsync(TMessage message);
    
    public virtual Task CompensateAsync(TMessage message) => 
        Context.CompensateAndBubbleUp<TMessage>();
}