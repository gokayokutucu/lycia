using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Handlers;

/// <summary>
/// Represents the starting point of an orchestration-based saga.
/// Handles the initial message in a coordinated saga and initializes saga state.
/// Use this as a base for coordinated saga starters.
/// </summary>
public abstract class StartCoordinatedSagaHandler<TMessage, TResponse, TSagaData> :
    ISagaStartHandler<TMessage, TSagaData>
    where TMessage : IMessage
    where TResponse : IResponse<TMessage>
    where TSagaData : SagaData, new()
{
    protected ISagaContext<TMessage, TSagaData> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<TMessage, TSagaData> context)
    {
        Context = context;
    }

    public abstract Task HandleStartAsync(TMessage message);
    
    protected async Task HandleAsyncInternal(TMessage message)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        try
        {
            await HandleStartAsync(message);  // Actual business logic
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
            await CompensateStartAsync(message);  // Actual business logic
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
}