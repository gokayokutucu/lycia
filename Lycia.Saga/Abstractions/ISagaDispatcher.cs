using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaDispatcher
{
    Task DispatchAsync<TMessage>(TMessage message) where TMessage : IMessage;

    Task DispatchAsync<TCommand, TResponse>(TResponse message)
        where TCommand : IMessage
        where TResponse : IResponse<TCommand>;
}