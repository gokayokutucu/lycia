using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers.Abstractions;

namespace Lycia.Saga.Handlers;

/// <summary>
/// Represents the starting point of a choreography-based saga.
/// Handles the initial message in a choreography saga and is responsible for initializing the saga flow.
/// Use this as a base for handlers that trigger new saga instances.
/// </summary>
public abstract class StartReactiveSagaHandler<TMessage> :   
    ISagaStartHandler<TMessage>
    where TMessage : IMessage
{
    protected ISagaContext<IMessage> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<IMessage> context)
    {
        Context = context;
    }

    public abstract Task HandleStartAsync(TMessage message, CancellationToken cancellationToken = default);
    
    protected async Task HandleAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await HandleStartAsync(message, cancellationToken);  // Actual business logic
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
            await CompensateStartAsync(message, cancellationToken);  // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsCompensationFailed<TMessage>(cancellationToken);
        }
    }
    
    public virtual Task CompensateStartAsync(TMessage message, CancellationToken cancellationToken = default)    
    {
        return Task.CompletedTask;
    }
    protected Task MarkAsComplete(CancellationToken cancellationToken = default) => Context.MarkAsComplete<TMessage>(cancellationToken);
    protected Task MarkAsFailed(CancellationToken cancellationToken = default) => Context.MarkAsFailed<TMessage>(cancellationToken);
    protected Task MarkAsCompensationFailed(CancellationToken cancellationToken = default) => Context.MarkAsCompensationFailed<TMessage>(cancellationToken);
    protected Task<bool> IsAlreadyCompleted(CancellationToken cancellationToken = default) => Context.IsAlreadyCompleted<TMessage>(cancellationToken);
}