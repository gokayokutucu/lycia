// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Helpers;
using Lycia.Messaging;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Extensions.Helpers;

/// <summary>Maps Lycia message semantics to RabbitMQ exchange and binding semantics.</summary>
public static class RabbitMqTopology
{
    /// <summary>RabbitMQ direct exchange type used for commands and responses.</summary>
    public const string DirectExchange = "direct";
    /// <summary>RabbitMQ fanout exchange type used for events.</summary>
    public const string FanoutExchange = "fanout";

    /// <summary>Returns the exchange type required by a message contract.</summary>
    public static string GetExchangeType(Type messageType) =>
        MessageKindResolver.Resolve(messageType) == MessageKind.Event ? FanoutExchange : DirectExchange;

    /// <summary>Returns the concrete binding key for the logical consumer.</summary>
    public static string GetBindingKey(Type messageType, string applicationId)
    {
        switch (MessageKindResolver.Resolve(messageType))
        {
            case MessageKind.Command:
                return MessagingNamingHelper.GetCommandRoutingKey(messageType);
            case MessageKind.Response:
                return applicationId;
            default:
                return string.Empty;
        }
    }

    /// <summary>Returns the concrete owner or requester key used to publish a message.</summary>
    public static string GetPublishKey(object message, Type messageType)
    {
        switch (MessageKindResolver.Resolve(messageType))
        {
            case MessageKind.Command:
                return MessagingNamingHelper.GetCommandRoutingKey(messageType);
            case MessageKind.Response:
                return GetResponseDestination(message, messageType);
            default:
                return string.Empty;
        }
    }

    private static string GetResponseDestination(object message, Type messageType)
    {
        var metadata = message as IRequestRoutingMetadata;
        if (string.IsNullOrWhiteSpace(metadata?.ReplyTo))
            throw new InvalidOperationException(
                $"Response '{messageType.FullName}' does not contain a ReplyTo logical application endpoint.");
        return metadata!.ReplyTo!;
    }
}
