// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Runtime.CompilerServices;
using Lycia.Common.Messaging;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions;

public interface IEventBus
{
    /// <summary>Gets the canonical logical application endpoint shared by all replicas of this bus.</summary>
    string ApplicationId { get; }

    /// <summary>
    /// Sends a command to the one logical owner declared by its <see cref="ICommandEndpoint"/> marker.
    /// </summary>
    /// <typeparam name="TCommand">
    ///     The type of the command to send. Must implement <see cref="ICommand"/>.
    /// </typeparam>
    /// <param name="command">
    ///     The command object to send to the target consumer.
    /// </param>
    /// <param name="handlerType">
    ///     Optional handler context for correlation or tracing. It does not select the transport destination.
    /// </param>
    /// <param name="sagaId">
    ///     (Optional) The saga identifier associated with this command, if part of a saga. Used for correlation or tracing.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    ///     A <see cref="Task"/> representing the asynchronous send operation.
    /// </returns>
    Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TCommand : ICommand;

    /// <summary>Sends a response only to the logical endpoint waiting for the request.</summary>
    Task Respond<TRequest, TResponse>(
        TRequest request,
        TResponse response,
        Type? handlerType = null,
        Guid? sagaId = null,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IResponse<TRequest>;

    /// <summary>
    /// Publishes an event to all interested subscribers. Response contracts are not valid broadcast events.
    /// </summary>
    /// <typeparam name="TEvent">
    ///     The type of the event to publish. Must implement <see cref="IEvent"/>.
    /// </typeparam>
    /// <param name="event">
    ///     The event object to broadcast to all subscribers.
    /// </param>
    /// <param name="handlerType">
    ///     Optional handler context for correlation or tracing. Event subscriptions are derived during discovery.
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
    /// <param name="autoAck"></param>
    /// <param name="cancellationToken">
    ///     A cancellation token that can be used to stop message consumption gracefully.
    /// </param>
    /// <returns>
    ///     An asynchronous stream (<see cref="IAsyncEnumerable{T}"/>) yielding a tuple consisting of the raw message body (<see cref="byte[]"/>) and its corresponding <see cref="Type"/>.
    /// </returns>
    IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes messages from a message broker with explicit acknowledgment support.
    /// Allows manual handling of acknowledgment and negative acknowledgment of messages
    /// for fine-grained control over message processing.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to observe while waiting for the asynchronous operation to complete.
    /// This token can be used to cancel the consumption process.
    /// </param>
    /// <returns>
    /// An asynchronous enumerable of <see cref="IncomingMessage"/> objects that contain
    /// the message data, metadata, and acknowledgment methods for handling consumed messages.
    /// </returns>
    IAsyncEnumerable<IncomingMessage> ConsumeWithAckAsync(
        CancellationToken cancellationToken = default);
}
