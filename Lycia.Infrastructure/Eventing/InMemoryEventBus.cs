using Lycia.Infrastructure.Abstractions;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Infrastructure.Eventing;

/// <summary>
/// Simple in-memory event bus that directly dispatches messages to registered handlers.
/// Suitable for development and testing only.
/// </summary>
public class InMemoryEventBus(ISagaDispatcher sagaDispatcher) : IEventBus
{
    public async Task Send<TCommand>(TCommand command, Guid sagaId) where TCommand : ICommand
    {
        await sagaDispatcher.DispatchAsync(command);
    }

    public async Task Publish<TEvent>(TEvent @event, Guid sagaId) where TEvent : IEvent
    {
        await sagaDispatcher.DispatchAsync(@event);
    }
}