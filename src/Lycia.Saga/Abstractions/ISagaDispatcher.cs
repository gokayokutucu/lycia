// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaDispatcher
{
    Task DispatchAsync<TMessage>(TMessage message, Type? handlerType, Guid? sagaId, CancellationToken cancellationToken) where TMessage : IMessage;

    Task DispatchAsync<TMessage, TResponse>(TResponse message, Type? handlerType, Guid? sagaId, CancellationToken cancellationToken)
        where TMessage : IMessage
        where TResponse : IResponse<TMessage>;
}