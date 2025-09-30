// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Seq.Consumer.Sagas;

public class ShippingSagaHandler : 
    CoordinatedSagaHandler<PaymentProcessedEvent, CreateOrderSagaData>
{
    public override async Task HandleAsync(PaymentProcessedEvent message, CancellationToken cancellationToken = default)
    {
        // Simulate shipping step
        var shipped = true; // Simulate logic

        if (!shipped)
        {
            // Shipping failed
            await Context.MarkAsFailed<PaymentProcessedEvent>(cancellationToken);
            return;
        }

        // Shipping succeeded, complete the saga or trigger next step if needed
        await Context.MarkAsComplete<PaymentProcessedEvent>();
    }

    public override async Task CompensateAsync(PaymentProcessedEvent message, CancellationToken cancellationToken = default)
    {
        // Compensation logic: recall shipment, notify customer, etc.
        Context.Data.ShippingReversed = true;
        await Context.CompensateAndBubbleUp<PaymentProcessedEvent>(cancellationToken);
    }
}