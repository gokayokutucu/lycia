// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.Services;

namespace Sample.Order.Choreography.Consumer.Sagas;

public class PaymentSagaHandler : ReactiveSagaHandler<InventoryReservedEvent>
{
    public override async Task HandleAsync(InventoryReservedEvent evt, CancellationToken cancellationToken = default)
    {
        var ok = PaymentService.SimulatePayment(false);
        if (!ok)
        {
            await Context.Publish(new PaymentFailedEvent
            {
                OrderId = evt.OrderId,
                ParentMessageId = evt.MessageId
            }, cancellationToken);

            // Mark only this step as failed (step logging/metrics)
            await Context.MarkAsFailed<InventoryReservedEvent>(cancellationToken);
            return;
        }

        await Context.Publish(new PaymentSucceededEvent
        {
            OrderId = evt.OrderId,
            ParentMessageId = evt.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<InventoryReservedEvent>();
    }
}