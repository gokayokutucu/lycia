using System.Runtime.CompilerServices;
using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface IEventBus
{
    /// <summary>
    /// Sends a command to a specific consumer (queue) for point-to-point communication in sagas or workflows.
    /// </summary>
    /// <typeparam name="TCommand">
    ///     The type of the command to send. Must implement <see cref="ICommand"/>.
    /// </typeparam>
    /// <param name="command">
    ///     The command object to send to the target consumer.
    /// </param>
    /// <param name="handlerType">
    ///     (Optional) The type of the handler that will process this command, if known. Used for correlation or tracing.
    /// </param>
    /// <param name="sagaId">
    ///     (Optional) The saga identifier associated with this command, if part of a saga. Used for correlation or tracing.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    ///     A <see cref="Task"/> representing the asynchronous send operation.
    /// </returns>
    Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TCommand : ICommand;

    /// <summary>
    /// Publishes an event to all interested subscribers for broadcasting state changes or domain events in the system.
    /// </summary>
    /// <typeparam name="TEvent">
    ///     The type of the event to publish. Must implement <see cref="IEvent"/>.
    /// </typeparam>
    /// <param name="event">
    ///     The event object to broadcast to all subscribers.
    /// </param>
    /// <param name="handlerType">
    ///     (Optional) The type of the handler that will process this event, if known. Used for routing or filtering.
    /// </param>
    /// <param name="sagaId">
    ///     (Optional) The saga identifier associated with this event, if part of a saga. Used for correlation or tracing.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    ///     A <see cref="Task"/> representing the asynchronous publish operation.
    /// </returns>
    Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TEvent : IEvent;
    
    /// <summary>
    /// Asynchronously consumes messages from the registered queues and yields each message as a tuple containing the raw message body and its resolved message type.
    /// Intended for use in background listeners or workers to process incoming commands and events in a strongly-typed, streaming manner.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A cancellation token that can be used to stop message consumption gracefully.
    /// </param>
    /// <returns>
    ///     An asynchronous stream (<see cref="IAsyncEnumerable{T}"/>) yielding a tuple consisting of the raw message body (<see cref="byte[]"/>) and its corresponding <see cref="Type"/>.
    /// </returns>
    IAsyncEnumerable<(byte[] Body, Type MessageType)> ConsumeAsync(CancellationToken cancellationToken);
}