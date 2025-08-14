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
    protected ISagaContext<IMessage> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<IMessage> context)
    {
        Context = context;
    }
    
    protected async Task HandleAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await HandleAsync(message, cancellationToken);  // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsFailed<TMessage>(cancellationToken);
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

    public virtual Task CompensateAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
    
    protected Task MarkAsComplete(CancellationToken cancellationToken = default) => Context.MarkAsComplete<TMessage>(cancellationToken);
    protected Task MarkAsFailed(CancellationToken cancellationToken = default) => Context.MarkAsFailed<TMessage>(cancellationToken);
    protected Task MarkAsCompensationFailed(CancellationToken cancellationToken = default) => Context.MarkAsCompensationFailed<TMessage>(cancellationToken);
    protected Task<bool> IsAlreadyCompleted(CancellationToken cancellationToken = default) => Context.IsAlreadyCompleted<TMessage>(cancellationToken);
}