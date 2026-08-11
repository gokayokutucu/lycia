// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;

namespace Lycia.Saga.Contexts;

/// <summary>Preserves the historical direct-to-transport behavior when no Outbox is configured.</summary>
public sealed class DirectOutgoingMessagePipeline(IEventBus eventBus) : IOutgoingMessagePipeline
{
    /// <inheritdoc />
    public Task Send<TCommand>(TCommand command, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        eventBus.Send(command, handlerType, sagaId, cancellationToken);

    /// <inheritdoc />
    public Task Publish<TEvent>(TEvent message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default) where TEvent : IEvent =>
        eventBus.Publish(message, handlerType, sagaId, cancellationToken);

    /// <inheritdoc />
    public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage where TResponse : IResponse<TRequest> =>
        eventBus.Respond(request, response, handlerType, sagaId, cancellationToken);
}
