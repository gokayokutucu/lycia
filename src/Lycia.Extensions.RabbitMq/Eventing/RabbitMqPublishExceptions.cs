// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Extensions.Eventing;

/// <summary>
/// A RabbitMQ publish did not complete with a positive publisher confirm. The derived type says whether the
/// outcome is known: <see cref="RabbitMqPublishNackedException"/> and
/// <see cref="RabbitMqUnroutableMessageException"/> mean the broker did not take the message, while
/// <see cref="RabbitMqPublishOutcomeUnknownException"/> means it may have.
/// </summary>
public abstract class RabbitMqPublishException : Exception
{
    /// <summary>Creates the exception for a publish to <paramref name="exchange"/> with <paramref name="routingKey"/>.</summary>
    protected RabbitMqPublishException(string message, string exchange, string routingKey, Exception? innerException = null)
        : base(message, innerException)
    {
        Exchange = exchange;
        RoutingKey = routingKey;
    }

    /// <summary>Gets the exchange the message was published to.</summary>
    public string Exchange { get; }

    /// <summary>Gets the routing key the message was published with.</summary>
    public string RoutingKey { get; }
}

/// <summary>
/// RabbitMQ negatively acknowledged the publish (<c>basic.nack</c>). RabbitMQ only does this when an internal
/// error occurs in the process responsible for a queue, for example a queue that rejects publishes because it
/// is full. The broker did not take responsibility for the message.
/// </summary>
public sealed class RabbitMqPublishNackedException(string exchange, string routingKey, Exception? innerException = null)
    : RabbitMqPublishException(
        $"RabbitMQ rejected the publish to exchange '{exchange}' (routing key '{routingKey}') with basic.nack.",
        exchange, routingKey, innerException);

/// <summary>
/// The message was published as mandatory and no queue received it (<c>basic.return</c>). It was not
/// delivered anywhere, so it is not a confirmed delivery even though RabbitMQ also acknowledges the publish.
/// </summary>
public sealed class RabbitMqUnroutableMessageException(string exchange, string routingKey, Exception? innerException = null)
    : RabbitMqPublishException(
        $"RabbitMQ returned the message published to exchange '{exchange}' (routing key '{routingKey}'): " +
        "no queue is bound that receives it.",
        exchange, routingKey, innerException);

/// <summary>
/// The publish was sent but its outcome could not be established: the confirmation did not arrive within
/// the timeout, or the channel or connection was lost while waiting. The broker may already have accepted the
/// message, so this is deliberately not reported as a definite failure; the Outbox keeps the message and
/// publishes it again with the same <c>MessageId</c> (at-least-once).
/// </summary>
public sealed class RabbitMqPublishOutcomeUnknownException(string exchange, string routingKey, string reason, Exception? innerException = null)
    : RabbitMqPublishException(
        $"The outcome of the publish to exchange '{exchange}' (routing key '{routingKey}') is unknown: {reason}",
        exchange, routingKey, innerException);
