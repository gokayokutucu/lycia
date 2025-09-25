// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Abstractions;
using Lycia.Configurations;
using Lycia.Handlers.Abstractions;
using Lycia.Messaging;
using Microsoft.Extensions.Options;

namespace Lycia.Handlers;

/// <summary>
/// Represents a single choreography step in a saga workflow (step handler, not a starter).
/// Implements business logic for a saga message without managing stateful saga data.
/// Use this as a base for choreography saga steps that react to incoming events.
/// </summary>
public abstract class ReactiveSagaHandler<TMessage> :  
    ISagaHandler<TMessage>
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
    
    protected async Task HandleAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (EnforceIdempotency &&
                await Context.IsAlreadyCompleted<TMessage>())
                return;
            
            await HandleAsync(message, cancellationToken);  // Actual business logic
        }
        catch (OperationCanceledException ex)
        {
            await Context.MarkAsCancelled<TMessage>(ex);
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<TMessage>(ex, cancellationToken);
        }
    }

    protected async Task CompensateAsyncInternal(TMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CompensateAsync(message, cancellationToken);  // Actual business logic
        }
        catch (OperationCanceledException ex)
        {
            await Context.MarkAsCancelled<TMessage>(ex);
        }
        catch (Exception ex)
        {
            await Context.MarkAsCompensationFailed<TMessage>(ex);
        }
    }

    public abstract Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);

    public virtual Task CompensateAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
    
    protected Task MarkAsComplete(CancellationToken cancellationToken = default) => Context.MarkAsComplete<TMessage>();
    protected Task MarkAsFailed(CancellationToken cancellationToken = default) => Context.MarkAsFailed<TMessage>(cancellationToken);
    protected Task MarkAsCompensationFailed(CancellationToken cancellationToken = default) => Context.MarkAsCompensationFailed<TMessage>();
    protected Task<bool> IsAlreadyCompleted(CancellationToken cancellationToken = default) => Context.IsAlreadyCompleted<TMessage>();
}