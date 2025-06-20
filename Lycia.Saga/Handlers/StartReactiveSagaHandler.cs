using Lycia.Messaging;
using Lycia.Saga.Abstractions;

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
    protected ISagaContext<TMessage> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<TMessage> context)
    {
        Context = context;
    }

    public abstract Task HandleStartAsync(TMessage message);
    public virtual Task CompensateStartAsync(TMessage message)    
    {
        return Task.CompletedTask;
    }
    protected Task MarkAsComplete() => Context.MarkAsComplete<TMessage>();
    protected Task MarkAsFailed() => Context.MarkAsFailed<TMessage>();
    protected Task MarkAsCompensationFailed() => Context.MarkAsCompensationFailed<TMessage>();
    protected Task<bool> IsAlreadyCompleted() => Context.IsAlreadyCompleted<TMessage>();
}