using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Handlers;

/// <summary>
/// Represents a step in an orchestration-based saga.
/// Handles a saga message and maintains saga state via saga data.
/// Use this as a base for coordinated (stateful) saga step handlers.
/// </summary>
public abstract class CoordinatedSagaHandler<TMessage, TResponse, TSagaData> :
    ISagaHandler<TMessage, TSagaData>
    where TMessage : IMessage
    where TResponse : IResponse<TMessage>
    where TSagaData : SagaData, new()
{
    protected ISagaContext<TMessage, TSagaData> Context { get; private set; } = null!;

    public void Initialize(ISagaContext<TMessage, TSagaData> context)
    {
        Context = context;
    }

    public async Task HandleAsyncInternal(TMessage message)
    {
        Context.RegisterStepMessage(message); // Mapping the message to the saga context
        await HandleAsync(message);           // Actual business logic
    }

    protected abstract Task HandleAsync(TMessage message);
    
    public virtual Task CompensateAsync(TMessage message)
    {
        return Task.CompletedTask;
    }
}