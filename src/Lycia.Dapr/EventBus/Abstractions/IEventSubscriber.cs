using Lycia.Dapr.Messages.Abstractions;

namespace Lycia.Dapr.EventBus.Abstractions;

public interface IEventSubscriber
{
    Task SubscribeAsync<TEvent, TEventHandler>(TEventHandler handler,CancellationToken cancellationToken)
        where TEvent: IEvent
        where TEventHandler:  IEventHandler<TEvent>;
   
}