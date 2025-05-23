using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Handlers;

public abstract class ReactiveSagaHandler<TMessage> :   
    ISagaHandlerWithContext<TMessage>, 
    ISagaStartHandler<TMessage>
    where TMessage : IMessage
{
    protected ISagaContext<TMessage> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<TMessage> context)
    {
        Context = context;
    }

    public abstract Task HandleStartAsync(TMessage message);

    protected Task MarkAsComplete() => Context.MarkAsComplete<TMessage>();
    protected Task MarkAsFailed() => Context.MarkAsFailed<TMessage>();
    protected Task MarkAsCompensationFailed() => Context.MarkAsCompensationFailed<TMessage>();
    protected Task<bool> IsAlreadyCompleted() => Context.IsAlreadyCompleted<TMessage>();
}