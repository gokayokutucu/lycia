using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaDispatcher
{
    Task DispatchAsync<TMessage>(TMessage message, Guid? sagaId, CancellationToken cancellationToken) where TMessage : IMessage;

    Task DispatchAsync<TMessage, TResponse>(TResponse message, Guid? sagaId, CancellationToken cancellationToken)
        where TMessage : IMessage
        where TResponse : IResponse<TMessage>;
}