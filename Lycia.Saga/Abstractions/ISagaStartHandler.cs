using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaStartHandler<in TMessage>
    where TMessage : IMessage
{
    Task HandleStartAsync(TMessage message);
}