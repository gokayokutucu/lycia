using Lycia.Messaging;

namespace Lycia.Saga.Handlers.Abstractions;

public interface ISagaCompensationHandler<in TMessage> where TMessage : IMessage
{
    Task CompensateAsync(TMessage message);
}