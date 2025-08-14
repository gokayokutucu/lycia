using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Configurations;
using Lycia.Saga.Handlers.Abstractions;
using Microsoft.Extensions.Options;

namespace Lycia.Saga.Handlers;

/// <summary>
/// Represents the starting point of an orchestration-based saga.
/// Handles the initial message in a coordinated saga and initializes saga state.
/// Use this as a base for coordinated saga starters.
/// </summary>
public abstract class StartCoordinatedSagaHandler<TMessage, TSagaData> :
    ISagaStartHandler<TMessage, TSagaData>
    where TMessage : IMessage
    where TSagaData : SagaData
{
    protected ISagaContext<IMessage, TSagaData> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<IMessage, TSagaData> context, IOptions<SagaOptions> sagaOptions)
    {
        Context = context;
    }

    public abstract Task HandleStartAsync(TMessage message);

    protected async Task HandleAsyncInternal(TMessage message)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await HandleStartAsync(message); // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsFailed<TMessage>();
        }
    }

    protected async Task CompensateAsyncInternal(TMessage message)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await CompensateStartAsync(message); // Actual business logic
        }
        catch (Exception)
        {
            await Context.MarkAsCompensationFailed<TMessage>();
        }
    }

    public virtual Task CompensateStartAsync(TMessage message)
    {
        return Task.CompletedTask;
    }
    
    protected Task MarkAsComplete(CancellationToken cancellationToken = default) => Context.MarkAsComplete<TMessage>(cancellationToken);
    protected Task MarkAsFailed(CancellationToken cancellationToken = default) => Context.MarkAsFailed<TMessage>(cancellationToken);
    protected Task MarkAsCompensationFailed(CancellationToken cancellationToken = default) => Context.MarkAsCompensationFailed<TMessage>(cancellationToken);
    protected Task<bool> IsAlreadyCompleted(CancellationToken cancellationToken = default) => Context.IsAlreadyCompleted<TMessage>(cancellationToken);
}