// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Extensions;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Messaging;

/// <summary>Transport-independent semantic message categories.</summary>
public enum MessageKind
{
    /// <summary>A message without a more specific Lycia semantic.</summary>
    Message,
    /// <summary>A point-to-point intention sent to one owner.</summary>
    Command,
    /// <summary>A published fact delivered to independent subscriptions.</summary>
    Event,
    /// <summary>A reply targeted to the requesting logical application.</summary>
    Response
}

/// <summary>Classifies message contracts without transport-specific branching.</summary>
public static class MessageKindResolver
{
    /// <summary>Returns the semantic category for <paramref name="messageType"/>.</summary>
    public static MessageKind Resolve(Type messageType)
    {
        if (messageType == null) throw new ArgumentNullException(nameof(messageType));
        if (messageType.IsSubclassOfResponseBase()) return MessageKind.Response;
        if (typeof(ICommand).IsAssignableFrom(messageType)) return MessageKind.Command;
        if (typeof(IEvent).IsAssignableFrom(messageType)) return MessageKind.Event;
        return MessageKind.Message;
    }

    /// <summary>Returns the lowercase naming prefix for <paramref name="messageType"/>.</summary>
    public static string GetPrefix(Type messageType) => Resolve(messageType).ToString().ToLowerInvariant();
}
