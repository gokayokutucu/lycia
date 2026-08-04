// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Messaging;

namespace Lycia.Helpers;

/// <summary>Provides stable logical names shared by Lycia transport implementations.</summary>
public static class MessagingNamingHelper
{
    /// <summary>Returns the stable per-message-type exchange name.</summary>
    public static string GetExchangeName(Type messageType) =>
        $"{MessageKindResolver.GetPrefix(messageType)}.{messageType.Name}";

    /// <summary>Returns the logical command owner derived from its endpoint marker.</summary>
    public static string GetCommandRoutingKey(Type commandType) =>
        CommandEndpointResolver.Default.Resolve(commandType);

    /// <summary>Returns <c>command.{MessageType}.{ApplicationId}</c>.</summary>
    public static string GetCommandQueueName(Type commandType, string? applicationId)
    {
        ValidateApplicationId(applicationId);
        return $"command.{commandType.Name}.{applicationId}";
    }

    /// <summary>Returns <c>event.{MessageType}.{HandlerType}.{ApplicationId}</c>.</summary>
    public static string GetEventSubscriptionQueueName(Type eventType, Type handlerType, string? applicationId)
    {
        if (handlerType == null) throw new ArgumentNullException(nameof(handlerType));
        ValidateApplicationId(applicationId);
        return $"event.{eventType.Name}.{handlerType.Name}.{applicationId}";
    }

    /// <summary>Returns <c>response.{MessageType}.{ApplicationId}</c>.</summary>
    public static string GetResponseQueueName(Type responseType, string? applicationId)
    {
        ValidateApplicationId(applicationId);
        return $"response.{responseType.Name}.{applicationId}";
    }

    /// <summary>Returns the logical queue name appropriate for the message kind.</summary>
    public static string GetQueueName(Type messageType, Type handlerType, string? applicationId)
    {
        switch (MessageKindResolver.Resolve(messageType))
        {
            case MessageKind.Command:
                return GetCommandQueueName(messageType, applicationId);
            case MessageKind.Event:
                return GetEventSubscriptionQueueName(messageType, handlerType, applicationId);
            case MessageKind.Response:
                return GetResponseQueueName(messageType, applicationId);
            default:
                ValidateApplicationId(applicationId);
                return $"message.{messageType.Name}.{handlerType.Name}.{applicationId}";
        }
    }

    /// <summary>Compatibility alias for <see cref="GetQueueName"/>.</summary>
    [Obsolete("Use GetQueueName or a message-kind-specific naming method. Topic wildcard routing is no longer used.")]
    public static string GetRoutingKey(Type messageType, Type handlerType, string? applicationId) =>
        GetQueueName(messageType, handlerType, applicationId);

    /// <summary>Compatibility helper; returns a key only for commands.</summary>
    [Obsolete("Use GetCommandRoutingKey for commands. Events use fanout exchanges and do not require a routing pattern.")]
    public static string GetTopicRoutingKey(Type messageType) =>
        MessageKindResolver.Resolve(messageType) == MessageKind.Command
            ? GetCommandRoutingKey(messageType)
            : string.Empty;

    private static void ValidateApplicationId(string? applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            throw new ArgumentException("ApplicationId cannot be null or empty.", nameof(applicationId));
    }
}
