namespace Lycia.Dapr.Messages.Abstractions;

public interface IEventHandler
{
    Task Handle(IEvent @event);
}

public interface IEventHandler<in TEvent> : IEventHandler
    where TEvent : IEvent
{
    Task Handle(TEvent @event);
}