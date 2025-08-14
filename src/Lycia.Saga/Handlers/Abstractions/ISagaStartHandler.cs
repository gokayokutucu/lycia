using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Handlers.Abstractions;

public interface ISagaStartHandler<TMessage>
    where TMessage : IMessage
{
    void Initialize(ISagaContext<IMessage> context);
}

public interface ISagaStartHandler<TMessage, TSagaData>
    where TMessage: IMessage
    where TSagaData : SagaData
{
    void Initialize(ISagaContext<IMessage, TSagaData> context);
}