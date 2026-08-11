// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>
/// Single selection point for outgoing saga operations. The direct implementation delegates to the
/// event bus; the durable implementation captures the same semantic in an outbox store.
/// </summary>
public interface IOutgoingMessagePipeline
{
    /// <summary>Sends a command directly or captures it durably, depending on configured policy.</summary>
    Task Send<TCommand>(TCommand command, Type? handlerType, Guid? sagaId, CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>Publishes an event directly or captures it durably, depending on configured policy.</summary>
    Task Publish<TEvent>(TEvent message, Type? handlerType, Guid? sagaId, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>Responds to a request directly or captures targeted response intent durably.</summary>
    Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IResponse<TRequest>;
}
