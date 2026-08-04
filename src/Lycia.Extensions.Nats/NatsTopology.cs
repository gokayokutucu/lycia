using Lycia.Helpers;
using Lycia.Messaging;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Extensions.Nats;

/// <summary>Stable NATS subjects and durable consumer identities for Lycia messages.</summary>
public static class NatsTopology
{
    /// <summary>Returns the command, event, or targeted response publish subject.</summary>
    public static string GetPublishSubject(object message, Type messageType)
    {
        switch (MessageKindResolver.Resolve(messageType))
        {
            case MessageKind.Command:
                return $"command.{MessagingNamingHelper.GetCommandRoutingKey(messageType)}.{messageType.Name}";
            case MessageKind.Response:
                return $"response.{GetResponseEndpoint(message, messageType)}.{messageType.Name}";
            default:
                return $"event.{messageType.Name}";
        }
    }

    /// <summary>Returns the subject consumed by one logical application subscription.</summary>
    public static string GetSubscriptionSubject(Type messageType, string applicationId)
    {
        switch (MessageKindResolver.Resolve(messageType))
        {
            case MessageKind.Command:
                return $"command.{MessagingNamingHelper.GetCommandRoutingKey(messageType)}.{messageType.Name}";
            case MessageKind.Response:
                return $"response.{EndpointIdentityNormalizer.Default.Normalize(applicationId)}.{messageType.Name}";
            default:
                return $"event.{messageType.Name}";
        }
    }

    /// <summary>Returns a JetStream-safe durable consumer name.</summary>
    public static string GetConsumerName(string logicalQueueName) => Sanitize(logicalQueueName);

    /// <summary>Returns a Core NATS queue group for an ephemeral logical subscription.</summary>
    public static string GetQueueGroup(string logicalQueueName) => $"lycia_{Sanitize(logicalQueueName)}";

    private static string GetResponseEndpoint(object message, Type messageType)
    {
        var metadata = message as IRequestRoutingMetadata;
        if (string.IsNullOrWhiteSpace(metadata?.ResponseEndpoint))
            throw new InvalidOperationException($"Response '{messageType.FullName}' does not contain ResponseEndpoint metadata.");
        return EndpointIdentityNormalizer.Default.Normalize(metadata!.ResponseEndpoint!);
    }

    private static string Sanitize(string value)
    {
        var chars = value.Select(character => char.IsLetterOrDigit(character) || character == '-' || character == '_'
            ? character
            : '_').ToArray();
        return new string(chars);
    }
}
