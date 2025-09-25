// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Abstractions;
using Lycia.Configurations;
using Lycia.Handlers.Abstractions;
using Lycia.Messaging;
using Microsoft.Extensions.Options;

namespace Lycia.Handlers;

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
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
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
    
    public virtual Task CompensateAsync(TMessage message, CancellationToken cancellationToken = default) => 
        Context.CompensateAndBubbleUp<TMessage>(cancellationToken);
    
    protected Task MarkAsComplete(CancellationToken cancellationToken = default) => Context.MarkAsComplete<TMessage>();
    protected Task MarkAsFailed(CancellationToken cancellationToken = default) => Context.MarkAsFailed<TMessage>(cancellationToken);
    protected Task MarkAsCompensationFailed(CancellationToken cancellationToken = default) => Context.MarkAsCompensationFailed<TMessage>();
    protected Task<bool> IsAlreadyCompleted(CancellationToken cancellationToken = default) => Context.IsAlreadyCompleted<TMessage>();
}