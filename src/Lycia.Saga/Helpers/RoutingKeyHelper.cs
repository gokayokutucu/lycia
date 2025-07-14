using Lycia.Messaging;

namespace Lycia.Saga.Helpers;

/// <summary>
/// RoutingKeyHelper provides methods for generating routing key strings used for exchange and queue naming conventions.
/// 
/// IMPORTANT: Routing keys must be unique enough to prevent cross-service message delivery conflicts in distributed saga/event-driven architectures.
/// - Always ensure handlerType and applicationId are unique per service or consumer.
/// - Failing to do so may cause multiple consumers to receive the same message or, worse, messages to be lost or misrouted.
/// - Do NOT hardcode applicationId or handler names across different services.
/// </summary>
public static class RoutingKeyHelper
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
    public static string GetRoutingKey(Type messageType, Type handlerType, string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            throw new ArgumentException("ApplicationId cannot be null or empty", nameof(applicationId));
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(handlerType); 
#else
        if (handlerType is null)
            throw new ArgumentNullException(nameof(handlerType), "Handler type cannot be null");
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
    public static string GetRoutingKey(Type messageType)
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
        else
        {
            prefix = "message";
        }

        // Full format: event.OrderCreatedEvent.#
        return $"{prefix}.{messageType.Name}.#";
    }
}