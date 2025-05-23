using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaHandlerWithContext<TInitialMessage> 
    where TInitialMessage : IMessage
{
    void Initialize(ISagaContext<TInitialMessage> context);
}

public interface ISagaHandlerWithContext<TInitialMessage, TSagaData> 
    where TInitialMessage: IMessage
    where TSagaData : SagaData
{
    void Initialize(ISagaContext<TInitialMessage, TSagaData> context);
}