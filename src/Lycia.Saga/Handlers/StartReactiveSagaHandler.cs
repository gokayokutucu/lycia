using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Configurations;
using Lycia.Saga.Handlers.Abstractions;
using Microsoft.Extensions.Options;

namespace Lycia.Saga.Handlers;

/// <summary>
/// Represents the starting point of a choreography-based saga.
/// Handles the initial message in a choreography saga and is responsible for initializing the saga flow.
/// Use this as a base for handlers that trigger new saga instances.
/// </summary>
public abstract class StartReactiveSagaHandler<TMessage> :   
    ISagaStartHandler<TMessage>
    where TMessage : IMessage
{
    protected ISagaContext<IMessage> Context { get; private set; } = null!;

    protected virtual bool EnforceIdempotency => 
        _sagaOptions?.DefaultIdempotency ?? true;

    private SagaOptions? _sagaOptions;
    public void Initialize(ISagaContext<IMessage> context, IOptions<SagaOptions> sagaOptions)
    {
        Context = context;
        _sagaOptions = sagaOptions.Value;
    }

    public abstract Task HandleStartAsync(TMessage message, CancellationToken cancellationToken = default);
    
    protected async Task HandleAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            if (EnforceIdempotency &&
                await Context.IsAlreadyCompleted<TMessage>(cancellationToken))
                return;
            
            await HandleStartAsync(message, cancellationToken);  // Actual business logic
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a failure; let it bubble (or just return if that's your policy)
            throw;
        }
        catch (Exception ex)
        {
            // Preserve failure details in your store/logs
            await Context.MarkAsFailed<TMessage>(ex, cancellationToken);
        }
    }
    
    protected async Task CompensateAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await CompensateStartAsync(message, cancellationToken);  // Actual business logic
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a failure; let it bubble (or just return if that's your policy)
            throw;
        }
        catch (Exception ex)
        {
            await Context.MarkAsCompensationFailed<TMessage>(ex, cancellationToken);
        }
    }
    
    public virtual Task CompensateStartAsync(TMessage message, CancellationToken cancellationToken = default)    
    {
        return Task.CompletedTask;
    }
    protected Task MarkAsComplete(CancellationToken cancellationToken = default) => Context.MarkAsComplete<TMessage>(cancellationToken);
    protected Task MarkAsFailed(CancellationToken cancellationToken = default) => Context.MarkAsFailed<TMessage>(cancellationToken);
    protected Task MarkAsCompensationFailed(CancellationToken cancellationToken = default) => Context.MarkAsCompensationFailed<TMessage>(cancellationToken);
    protected Task<bool> IsAlreadyCompleted(CancellationToken cancellationToken = default) => Context.IsAlreadyCompleted<TMessage>(cancellationToken);
}