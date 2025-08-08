using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers.Abstractions;

namespace Lycia.Saga.Handlers;

/// <summary>
/// Represents a step in an orchestration-based saga.
/// Handles a saga message and maintains saga state via saga data.
/// Use this as a base for coordinated (stateful) saga step handlers.
/// </summary>
public abstract class CoordinatedSagaHandler<TMessage, TSagaData> :
    ISagaHandler<TMessage, TSagaData>
    where TMessage : IMessage
    where TSagaData : SagaData
{
    protected ISagaContext<IMessage, TSagaData> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<IMessage, TSagaData> context)
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
        catch (Exception ex)
        {
            await Context.MarkAsFailed<TMessage>(new FailResponse()
            {
                Reason = "Saga step failed",
                ExceptionType = ex.GetType().Name,
                ExceptionDetail = ex.ToString()
            });
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