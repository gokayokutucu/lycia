using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaHandler<TMessage> 
    where TMessage : IMessage
{
    void Initialize(ISagaContext<TMessage> context);
}

public interface ISagaHandler<TMessage, TSagaData> 
    where TMessage: IMessage
    where TSagaData : SagaData
{
    void Initialize(ISagaContext<TMessage, TSagaData> context);
}