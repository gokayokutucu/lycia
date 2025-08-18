using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Configurations;
using Microsoft.Extensions.Options;

namespace Lycia.Saga.Handlers.Abstractions;

public interface ISagaHandler<TMessage> 
    where TMessage : IMessage
{
    void Initialize(ISagaContext<IMessage> context, IOptions<SagaOptions> sagaOptions);
}

public interface ISagaHandler<TMessage, TSagaData> 
    where TMessage: IMessage
    where TSagaData : SagaData
{
    void Initialize(ISagaContext<IMessage, TSagaData> context, IOptions<SagaOptions> sagaOptions);
}