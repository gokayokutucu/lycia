// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Extensions;

public static class MessageExtensions
{
    public static TOut Next<TIn, TOut>(this TIn previous)
        where TIn : IMessage
        where TOut : IMessage, new()
    {
        var next = new TOut
        {
            CorrelationId = previous.CorrelationId,
        };
        return next;
    }

    /// <summary>Copies requester routing metadata from the current saga step to a response.</summary>
    public static void PropagateResponseRouting(this IMessage outgoing, IMessage current)
    {
        if (!(outgoing is IRequestRoutingMetadata response) ||
            !(current is IRequestRoutingMetadata request))
            return;

        response.RequestId = request.RequestId == Guid.Empty ? current.MessageId : request.RequestId;
        if (string.IsNullOrWhiteSpace(response.ReplyTo))
            response.ReplyTo = request.ReplyTo;
    }
}
