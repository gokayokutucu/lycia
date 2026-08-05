using Lycia.Helpers;
using Lycia.Messaging;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Extensions.Kafka;

/// <summary>Stable Kafka topic, consumer-group, and partition-key conventions.</summary>
public static class KafkaTopology
{
    /// <summary>Returns the command, event, or targeted response publish topic.</summary>
    public static string GetPublishTopic(string prefix, object message, Type messageType)
    {
        switch (MessageKindResolver.Resolve(messageType))
        {
            case MessageKind.Command:
                return $"{prefix}.command.{MessagingNamingHelper.GetCommandRoutingKey(messageType)}.{messageType.Name}";
            case MessageKind.Response:
                return $"{prefix}.response.{GetResponseEndpoint(message, messageType)}.{messageType.Name}";
            default:
                return $"{prefix}.event.{messageType.Name}";
        }
    }

    /// <summary>Returns the topic consumed by one logical application subscription.</summary>
    public static string GetSubscriptionTopic(string prefix, Type messageType, string applicationId)
    {
        switch (MessageKindResolver.Resolve(messageType))
        {
            case MessageKind.Command:
                return $"{prefix}.command.{MessagingNamingHelper.GetCommandRoutingKey(messageType)}.{messageType.Name}";
            case MessageKind.Response:
                return $"{prefix}.response.{EndpointIdentityNormalizer.Default.Normalize(applicationId)}.{messageType.Name}";
            default:
                return $"{prefix}.event.{messageType.Name}";
        }
    }

    /// <summary>Returns a stable consumer group for the logical queue.</summary>
    public static string GetConsumerGroup(string prefix, string logicalQueueName) =>
        Sanitize($"{prefix}.{logicalQueueName}");

    /// <summary>Returns the partition key, preferring correlation, saga, then message identity.</summary>
    public static string GetPartitionKey(IMessage message)
    {
        if (message.CorrelationId != Guid.Empty) return message.CorrelationId.ToString("N");
        if (message.SagaId is { } sagaId && sagaId != Guid.Empty) return sagaId.ToString("N");
        return message.MessageId.ToString("N");
    }

    private static string GetResponseEndpoint(object message, Type messageType)
    {
        var metadata = message as IRequestRoutingMetadata;
        if (string.IsNullOrWhiteSpace(metadata?.ResponseEndpoint))
            throw new InvalidOperationException($"Response '{messageType.FullName}' does not contain ResponseEndpoint metadata.");
        return EndpointIdentityNormalizer.Default.Normalize(metadata!.ResponseEndpoint!);
    }

    private static string Sanitize(string value) => new(value.Select(character =>
        char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_'
            ? character
            : '_').ToArray());
}
