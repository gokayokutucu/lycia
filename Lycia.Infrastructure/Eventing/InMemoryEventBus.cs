using System; // For Lazy<T>
using Lycia.Infrastructure.Abstractions;
using Lycia.Messaging;
using Lycia.Saga.Abstractions; // ISagaDispatcher is likely here or in Lycia.Infrastructure.Abstractions

namespace Lycia.Infrastructure.Eventing;

/// <summary>
/// Simple in-memory event bus that directly dispatches messages to registered handlers.
/// Suitable for development and testing only.
/// </summary>
public class InMemoryEventBus : IEventBus
{
    private readonly Lazy<ISagaDispatcher> _sagaDispatcherLazy;

    public InMemoryEventBus(Lazy<ISagaDispatcher> sagaDispatcherLazy)
    {
        _sagaDispatcherLazy = sagaDispatcherLazy;
    }

    public async Task Send<TCommand>(TCommand command, Guid sagaId) where TCommand : ICommand
    {
        // The sagaId parameter passed to Send/Publish is not directly used by DispatchAsync,
        // as DispatchAsync typically resolves SagaId from the message properties or generates it.
        // The original implementation also didn't use the sagaId parameter in its call to DispatchAsync.
        await _sagaDispatcherLazy.Value.DispatchAsync(command);
    }

    public async Task Publish<TEvent>(TEvent @event, Guid sagaId) where TEvent : IEvent
    {
        await _sagaDispatcherLazy.Value.DispatchAsync(@event);
    }
}