// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Messaging.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.Services;

namespace Sample.Order.Choreography.Consumer.Sagas;

public sealed class InventorySagaHandler :
    ReactiveSagaHandler<OrderCreatedEvent>,
    ISagaCompensationHandler<PaymentFailedEvent>,
    ISagaCompensationHandler<OrderShippingFailedEvent>
{
    protected override bool EnforceIdempotency => true;

    public override async Task HandleAsync(
        OrderCreatedEvent message,
        CancellationToken cancellationToken = default)
    {
        // Reserve inventory idempotently.
        InventoryService.ReserveStock(message.OrderId);

        await Context.Publish(
            new InventoryReservedEvent
            {
                OrderId = message.OrderId
            },
            cancellationToken);

        await Context.MarkAsComplete<OrderCreatedEvent>();
    }

    public async Task CompensateAsync(
        PaymentFailedEvent failed,
        CancellationToken cancellationToken = default)
    {
        if (await Context.IsAlreadyCompleted<PaymentFailedEvent>())
        {
            return;
        }

        // Release inventory because payment failed.
        InventoryService.ReleaseStock(failed.OrderId);

        await Context.MarkAsCompensated<PaymentFailedEvent>();
    }

    public async Task CompensateAsync(
        OrderShippingFailedEvent failed,
        CancellationToken cancellationToken = default)
    {
        if (await Context.IsAlreadyCompleted<OrderShippingFailedEvent>())
        {
            return;
        }

        // Release inventory because the paid order could not be shipped.
        InventoryService.ReleaseStock(failed.OrderId);

        await Context.MarkAsCompensated<OrderShippingFailedEvent>();
    }
}