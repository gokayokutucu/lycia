// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;
using Lycia.Extensions;

namespace Lycia.Helpers;

/// <summary>
/// MessagingNamingHelper provides methods for generating naming conventions used for exchanges, queues, routing keys, and other message-related identifiers.
/// 
/// This helper is designed for distributed event-driven architectures such as Saga, DDD, CQRS, and similar patterns.
/// It ensures consistent and unique naming conventions for message routing, queue binding, and exchange declarations.
/// 
/// IMPORTANT: Naming conventions must be unique enough to prevent cross-service message delivery conflicts.
/// - Always ensure handlerType and applicationId are unique per service or consumer.
/// - Failing to do so may cause multiple consumers to receive the same message or, worse, messages to be lost or misrouted.
/// - Do NOT hardcode applicationId or handler names across different services.
/// </summary>
/// <remarks>
/// Usage:
/// - Use this helper for generating routing keys for both producers and consumers.
/// - Use for exchange and queue naming to maintain consistency across services.
/// 
/// Extensibility:
/// - This class can be extended in the future to support additional naming strategies, such as topics, dead-letter queues (DLQ), or other messaging patterns.
/// </remarks>
public static class MessagingNamingHelper
{
    /// <summary>
    /// Returns a unique routing key for the given message type, handler type, and application/service instance.
    /// Format: "{event|command|message}.{MessageType}.{HandlerType}.{ApplicationId}"
    ///
    /// - Use this method for **consumer/queue creation** (e.g., in AddLycia or during queue declaration in consumers).
    /// - The resulting routing key ensures that each consumer/handler/service instance receives only its intended messages.
    /// 
    /// WARNING: 
    /// - Always supply a unique <paramref name="applicationId"/> per microservice or app instance.
    /// - Use strongly-typed, non-generic handler types to avoid ambiguity.
    /// - Do NOT reuse applicationId across unrelated services, or queues will overlap.
    /// </summary>
    /// <param name="messageType">Type of the message (must inherit from EventBase, CommandBase, or IMessage).</param>
    /// <param name="handlerType">Type of the handler class consuming this message.</param>
    /// <param name="applicationId">A unique application or consumer/service identifier.</param>
    /// <returns>Unique routing key for queue/exchange binding.</returns>
    public static string GetRoutingKey(Type messageType, Type handlerType, string? applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            throw new ArgumentException("ApplicationId cannot be null or empty", nameof(applicationId));
#if NETSTANDARD2_0
        if (handlerType is null)
            throw new ArgumentNullException(nameof(handlerType), "Handler type cannot be null");
#else
        ArgumentNullException.ThrowIfNull(handlerType); 
#endif

        string prefix;
        if (messageType.IsSubclassOf(typeof(EventBase)))
        {
            prefix = "event";
        }
        else if (messageType.IsSubclassOf(typeof(CommandBase)))
        {
            prefix = "command";
        }
        else if (messageType.IsSubclassOfResponseBase())
        {
            prefix = "response";
        }
        else
        {
            prefix = "message";
        }

        // Full format: event.OrderCreatedEvent.CreateOrderSagaHandler.OrderService
        return $"{prefix}.{messageType.Name}.{handlerType.Name}.{applicationId}";
    }

    /// <summary>
    /// Returns a topic routing key pattern for publishing messages.
    /// Format: "{event|command|message}.{MessageType}.#"
    ///
    /// - Use this for **producer/publisher side** to send messages to all interested queues/consumers.
    /// - This method produces a wildcard pattern to allow "fan-out" to all queues matching the message type.
    ///
    /// WARNING: 
    /// - Do NOT use this for queue or consumer declarations.
    /// - Queues with overly broad (non-unique) routing keys may unintentionally receive unrelated messages.
    /// </summary>
    /// <param name="messageType">Type of the message to be published.</param>
    /// <returns>Topic routing key pattern for exchange publishing.</returns>
    public static string GetTopicRoutingKey(Type messageType)
    {
        string prefix;
        if (messageType.IsSubclassOf(typeof(EventBase)))
        {
            prefix = "event";
        }
        else if (messageType.IsSubclassOf(typeof(CommandBase)))
        {
            prefix = "command";
        }
        else if (messageType.IsSubclassOfResponseBase())
        {
            prefix = "response";
        }
        else
        {
            prefix = "message";
        }

        // Full format: event.OrderCreatedEvent.#
        return $"{prefix}.{messageType.Name}.#";
    }

    /// <summary>
    /// Returns the exchange name for the given message type.
    /// Format: "{event|command|message}.{MessageType}"
    /// 
    /// Use this for exchange declaration and binding.
    /// </summary>
    /// <param name="messageType">Type of the message.</param>
    /// <returns>Exchange name string.</returns>
    public static string GetExchangeName(Type messageType)
    {
        string prefix;
        if (messageType.IsSubclassOf(typeof(EventBase)))
        {
            prefix = "event";
        }
        else if (messageType.IsSubclassOf(typeof(CommandBase)))
        {
            prefix = "command";
        }
        else if (messageType.IsSubclassOfResponseBase())
        {
            prefix = "response";
        }
        else
        {
            prefix = "message";
        }

        return $"{prefix}.{messageType.Name}";
    }
}