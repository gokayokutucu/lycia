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

    protected async Task HandleAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
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
            }, cancellationToken);
        }
    }
    
    protected async Task CompensateAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await CompensateAsync(message, cancellationToken);  // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsCompensationFailed<TMessage>(cancellationToken);
        }
    }

    public abstract Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    
    public virtual Task CompensateAsync(TMessage message, CancellationToken cancellationToken = default) => 
        Context.CompensateAndBubbleUp<TMessage>(cancellationToken);
    
    protected Task MarkAsComplete(CancellationToken cancellationToken = default) => Context.MarkAsComplete<TMessage>(cancellationToken);
    protected Task MarkAsFailed(CancellationToken cancellationToken = default) => Context.MarkAsFailed<TMessage>(cancellationToken);
    protected Task MarkAsCompensationFailed(CancellationToken cancellationToken = default) => Context.MarkAsCompensationFailed<TMessage>(cancellationToken);
    protected Task<bool> IsAlreadyCompleted(CancellationToken cancellationToken = default) => Context.IsAlreadyCompleted<TMessage>(cancellationToken);
}