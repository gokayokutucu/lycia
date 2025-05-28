using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Handlers;


public abstract class CoordinatedSagaHandler<TInitialMessage, TResponse, TSagaData> :
    ISagaHandlerWithContext<TInitialMessage, TSagaData>
    where TInitialMessage : IMessage
    where TResponse : IResponse<TInitialMessage>
    where TSagaData : SagaData, new()
{
    protected ISagaContext<TInitialMessage, TSagaData> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<TInitialMessage, TSagaData> context)
    {
        Context = context;
    }
}