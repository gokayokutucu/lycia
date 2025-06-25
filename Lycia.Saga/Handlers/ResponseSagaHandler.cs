using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Handlers;

public abstract class ResponseSagaHandler<TResponse, TSagaData> :
    ISuccessResponseHandler<TResponse>,
    IFailResponseHandler<TResponse>
    where TResponse : ISuccessResponse<IMessage>, IFailResponse<IMessage>
    where TSagaData : SagaData, new()
{
    protected ISagaContext<IMessage, TSagaData> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<IMessage, TSagaData> context)
    {
        Context = context;
    }
    
    public async Task HandleSuccessResponseInternalAsync(TResponse response)
    {
        Context.RegisterStepMessage(response);
        await HandleSuccessResponseAsync(response);
    }

    public async Task HandleFailResponseInternalAsync(TResponse response, FailResponse fail)
    {
        Context.RegisterStepMessage(response);
        await HandleFailResponseAsync(response, fail);
    }

    public abstract Task HandleSuccessResponseAsync(TResponse response);
    public abstract Task HandleFailResponseAsync(TResponse response, FailResponse fail);
}