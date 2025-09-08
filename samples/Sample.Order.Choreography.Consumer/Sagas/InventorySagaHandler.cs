// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Sample.Shared.Messages.Events;
using Sample.Shared.Services;

namespace Sample.Order.Choreography.Consumer.Sagas;

public class InventorySagaHandler :
    ReactiveSagaHandler<OrderCreatedEvent>,
    ISagaCompensationHandler<PaymentFailedEvent>
{
    public override async Task HandleAsync(OrderCreatedEvent evt, CancellationToken cancellationToken = default)
    {
        // Reserve inventory
        await Context.Publish(new InventoryReservedEvent
        {
            OrderId = evt.OrderId,
            ParentMessageId = evt.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<OrderCreatedEvent>();
    }

    public override async Task CompensateAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            // Fix count of reserved stock
            InventoryService.ReleaseStock(message.OrderId);
            await Context.MarkAsCompensated<OrderCreatedEvent>();
        }
        catch (Exception ex)
        {
            await Context.MarkAsCompensationFailed<OrderCreatedEvent>();

            throw; 
        }
    }

    public Task CompensateAsync(PaymentFailedEvent failed, CancellationToken cancellationToken = default)
    {
        // Release reserved stock
        InventoryService.ReleaseStock(failed.OrderId);
        return Task.CompletedTask;
    }
}