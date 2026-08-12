// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Utility;

namespace Lycia.Saga.Extensions;

/// <summary>Central transport-independent identity propagation for outgoing saga messages.</summary>
public static class MessageIdentityExtensions
{
    /// <summary>Initializes a child command request from the current saga message.</summary>
    public static void PrepareCommand(this ICommand command, IMessage current, Guid sagaId, string responseEndpoint)
    {
        EnsureMessageId(command);
        command.RequestRouting().RequestId = command.MessageId;
        command.RequestRouting().ResponseEndpoint = responseEndpoint;
        PropagateWorkflow(command, current, sagaId);
    }

    /// <summary>Initializes a targeted response to a concrete request.</summary>
    public static void PrepareResponse<TRequest>(
        this IResponse<TRequest> response,
        TRequest request,
        Guid sagaId,
        string responseEndpoint)
        where TRequest : IMessage
    {
        if (response.MessageId == Guid.Empty || response.MessageId == request.MessageId)
            response.MessageId = GuidV7.NewGuidV7();

        response.RequestId = request.MessageId;
        var requestedEndpoint = request is IRequestRoutingMetadata routing &&
                                !string.IsNullOrWhiteSpace(routing.ResponseEndpoint)
            ? routing.ResponseEndpoint
            : responseEndpoint;
        response.ResponseEndpoint = EndpointIdentity.Normalize(requestedEndpoint);
        PropagateWorkflow(response, request, sagaId);
    }

    /// <summary>Initializes a broadcast event from the current saga message.</summary>
    public static void PrepareEvent(this IEvent @event, IMessage current, Guid sagaId)
    {
        if (@event is IResponse)
            throw new InvalidOperationException(
                $"Response '{@event.GetType().FullName}' cannot be published. Use Context.Respond(request, response)." );

        EnsureMessageId(@event);
        PropagateWorkflow(@event, current, sagaId);
    }

    private static void PropagateWorkflow(IMessage outgoing, IMessage current, Guid sagaId)
    {
        outgoing.CorrelationId = current.CorrelationId == Guid.Empty ? current.MessageId : current.CorrelationId;
        outgoing.CausationId = current.MessageId;
        outgoing.ParentMessageId = current.MessageId;
        outgoing.SagaId = sagaId;
    }

    private static void EnsureMessageId(IMessage message)
    {
        if (message.MessageId == Guid.Empty) message.MessageId = GuidV7.NewGuidV7();
    }

    private static IRequestRoutingMetadata RequestRouting(this ICommand command) =>
        command as IRequestRoutingMetadata
        ?? throw new InvalidOperationException(
            $"Command '{command.GetType().FullName}' must implement IRequestRoutingMetadata. Derive it from CommandBase.");
}
