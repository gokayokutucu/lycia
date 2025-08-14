using Lycia.Messaging;

namespace Lycia.Saga.Handlers.Abstractions;

public interface IResponseSagaHandler<in TResponse> :
    ISuccessResponseHandler<TResponse>,
    IFailResponseHandler<TResponse>
    where TResponse : IMessage
{

}