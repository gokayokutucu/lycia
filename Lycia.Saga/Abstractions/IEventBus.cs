using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface IEventBus
{
    /// <summary>
    /// Sends a command to a specific consumer. Used for point-to-point communication in sagas.
    /// </summary>
    Task Send<TCommand>(TCommand command, Guid? sagaId = null) where TCommand : ICommand;

    /// <summary>
    /// Publishes an event to all interested subscribers. Used for broadcasting state changes in sagas.
    /// </summary>
    Task Publish<TEvent>(TEvent @event, Guid? sagaId = null) where TEvent : IEvent;
}