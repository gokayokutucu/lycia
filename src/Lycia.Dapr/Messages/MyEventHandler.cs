using Lycia.Dapr.Messages.Abstractions;

namespace Lycia.Dapr.Messages;

public abstract class MyEventHandler<TEvent> : IEventHandler<TEvent>
    where TEvent : IEvent
{
    public abstract Task Handle(TEvent @event);

    public virtual Task Handle(IEvent @event)
    {
        return Handle((TEvent)@event);
    }
}