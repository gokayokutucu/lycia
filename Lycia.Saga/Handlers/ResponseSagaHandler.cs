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
    
    protected async Task HandleSuccessResponseInternalAsync(TResponse response)
    {
        Context.RegisterStepMessage(response); // Mapping the message to the saga context
        try
        {
            await HandleSuccessResponseAsync(response);  // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsFailed<TResponse>();
        }
    }

    protected async Task HandleFailResponseInternalAsync(TResponse response, FailResponse fail)
    {
        Context.RegisterStepMessage(response); // Mapping the message to the saga context
        try
        {
            await HandleFailResponseAsync(response, fail);  // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsFailed<TResponse>();
        }
    }

    public abstract Task HandleSuccessResponseAsync(TResponse response);
    public abstract Task HandleFailResponseAsync(TResponse response, FailResponse fail);
}