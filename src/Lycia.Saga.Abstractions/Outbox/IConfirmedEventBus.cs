// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>
/// Optional transport capability used by the outbox dispatcher when the underlying client can
/// positively acknowledge broker acceptance. Transports without this capability remain
/// <see cref="OutboxMessageStatus.ConfirmationUnknown"/> after an attempted dispatch.
/// </summary>
public interface IConfirmedEventBus
{
    /// <summary>Sends a command and returns only after positive broker acceptance.</summary>
    Task SendConfirmed<TCommand>(TCommand command, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default) where TCommand : ICommand;

    /// <summary>Publishes an event and returns only after positive broker acceptance.</summary>
    Task PublishConfirmed<TEvent>(TEvent message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default) where TEvent : IEvent;

    /// <summary>Routes a response and returns only after positive broker acceptance.</summary>
    Task RespondConfirmed<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage where TResponse : IResponse<TRequest>;
}
