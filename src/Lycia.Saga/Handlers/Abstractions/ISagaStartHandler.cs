using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Handlers.Abstractions;

public interface ISagaStartHandler<TMessage>
    where TMessage : IMessage
{
    void Initialize(ISagaContext<TMessage> context);
}

public interface ISagaStartHandler<TMessage, TSagaData>
    where TMessage: IMessage
    where TSagaData : new()
{
    void Initialize(ISagaContext<TMessage, TSagaData> context);
}