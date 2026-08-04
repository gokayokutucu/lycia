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

    /// <summary>Compatibility helper that copies request routing metadata without changing message identity.</summary>
    [Obsolete("Use Context.Respond(request, response), which initializes complete response identity and routing metadata.")]
    public static void PropagateResponseRouting(this IMessage outgoing, IMessage current)
    {
        if (!(outgoing is IRequestRoutingMetadata response) ||
            !(current is IRequestRoutingMetadata request))
            return;

        response.RequestId = current.MessageId;
        if (string.IsNullOrWhiteSpace(response.ResponseEndpoint))
            response.ResponseEndpoint = request.ResponseEndpoint;
    }
}
