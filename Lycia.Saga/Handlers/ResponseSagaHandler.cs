using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Handlers;

public abstract class ResponseSagaHandler<TResponse, TSagaData> :
    ISuccessResponseHandler<TResponse>,
    IFailResponseHandler<TResponse>,
    ISagaHandlerWithContext<IMessage, TSagaData>
    where TResponse : ISuccessResponse<IMessage>, IFailResponse<IMessage>
    where TSagaData : SagaData, new()
{
    protected ISagaContext<IMessage, TSagaData> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<IMessage, TSagaData> context)
    {
        Context = context;
    }

    public abstract Task HandleSuccessResponseAsync(TResponse response);
    public abstract Task HandleFailResponseAsync(TResponse response, FailResponse fail);
}