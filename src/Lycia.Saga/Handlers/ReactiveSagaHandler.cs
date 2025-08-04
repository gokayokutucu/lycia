using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers.Abstractions;

namespace Lycia.Saga.Handlers;

/// <summary>
/// Represents a single choreography step in a saga workflow (step handler, not a starter).
/// Implements business logic for a saga message without managing stateful saga data.
/// Use this as a base for choreography saga steps that react to incoming events.
/// </summary>
public abstract class ReactiveSagaHandler<TMessage> :  
    ISagaHandler<TMessage>
    where TMessage : IMessage
{
    protected ISagaContext<TMessage> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<TMessage> context)
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

    public virtual Task CompensateAsync(TMessage message)
    {
        return Task.CompletedTask;
    }
    
    protected Task MarkAsComplete() => Context.MarkAsComplete<TMessage>();
    protected Task MarkAsFailed() => Context.MarkAsFailed<TMessage>();
    protected Task MarkAsCompensationFailed() => Context.MarkAsCompensationFailed<TMessage>();
    protected Task<bool> IsAlreadyCompleted() => Context.IsAlreadyCompleted<TMessage>();
}