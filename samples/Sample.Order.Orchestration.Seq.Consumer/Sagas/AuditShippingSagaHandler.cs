// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Seq.Consumer.Sagas;

public class AuditShippingSagaHandler : 
    CoordinatedSagaHandler<PaymentProcessedEvent, CreateOrderSagaData>
{
    public override async Task HandleAsync(PaymentProcessedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            await Context.MarkAsFailed<PaymentProcessedEvent>(cancellationToken);
        }
        catch (Exception e)
        {
            Console.WriteLine($"🚨 Audit failed: {e.Message}");
            await Context.MarkAsFailed<PaymentProcessedEvent>(cancellationToken);
        }
    }

    public override async Task CompensateAsync(PaymentProcessedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            await Context.CompensateAndBubbleUp<PaymentProcessedEvent>(cancellationToken);
        }
        catch (Exception e)
        {
            await Context.MarkAsCompensationFailed<PaymentProcessedEvent>();
            throw;
        }
    }
}