using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Handlers;


public abstract class CoordinatedSagaHandler<TInitialMessage, TResponse, TSagaData> :
    ISagaHandlerWithContext<TInitialMessage, TSagaData>, ISagaStartHandler<TInitialMessage>
    where TInitialMessage : IMessage
    where TResponse : IResponse<TInitialMessage>
    where TSagaData : SagaData, new()
{
    protected ISagaContext<TInitialMessage, TSagaData> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<TInitialMessage, TSagaData> context)
    {
        Context = context;
    }

    public abstract Task HandleStartAsync(TInitialMessage message);

}