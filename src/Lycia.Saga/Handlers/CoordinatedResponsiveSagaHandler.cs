using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Configurations;
using Lycia.Saga.Handlers.Abstractions;
using Microsoft.Extensions.Options;

namespace Lycia.Saga.Handlers;

public abstract class CoordinatedResponsiveSagaHandler<TMessage, TResponse, TSagaData> :
    ISagaHandler<TMessage, TSagaData>
    where TMessage : IMessage
    where TResponse : IResponse<TMessage>
    where TSagaData : SagaData
{
    protected ISagaContext<IMessage, TSagaData> Context { get; private set; } = null!;
    
    public void Initialize(ISagaContext<IMessage, TSagaData> context, IOptions<SagaOptions> sagaOptions)
    {
        Context = context;
    }

    protected async Task HandleAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await HandleAsync(message, cancellationToken);  // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsFailed<TMessage>(cancellationToken);
        }
    }
    
    protected async Task CompensateAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await CompensateAsync(message, cancellationToken);  // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsCompensationFailed<TMessage>(cancellationToken);
        }
    }

    public abstract Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    
    public virtual Task CompensateAsync(TMessage message, CancellationToken cancellationToken = default) => 
        Context.CompensateAndBubbleUp<TMessage>(cancellationToken);
    
    protected Task MarkAsComplete(CancellationToken cancellationToken = default) => Context.MarkAsComplete<TMessage>(cancellationToken);
    protected Task MarkAsFailed(CancellationToken cancellationToken = default) => Context.MarkAsFailed<TMessage>(cancellationToken);
    protected Task MarkAsCompensationFailed(CancellationToken cancellationToken = default) => Context.MarkAsCompensationFailed<TMessage>(cancellationToken);
    protected Task<bool> IsAlreadyCompleted(CancellationToken cancellationToken = default) => Context.IsAlreadyCompleted<TMessage>(cancellationToken);
}