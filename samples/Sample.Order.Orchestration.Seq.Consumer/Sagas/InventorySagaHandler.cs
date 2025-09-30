// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Seq.Consumer.Sagas;
public class InventorySagaHandler : 
    CoordinatedSagaHandler<ReserveInventoryCommand, CreateOrderSagaData>
{
    public override async Task HandleAsync(ReserveInventoryCommand message, CancellationToken cancellationToken = default)
    {
        // Simulate inventory reservation
        var inventoryReserved = true; // Simulate logic
        
        if (!inventoryReserved)
        {
            // Inventory reservation failed
            await Context.MarkAsFailed<ReserveInventoryCommand>(cancellationToken);
            return;
        }
        
        // Inventory reserved, proceed to payment
        await Context.Send(new ProcessPaymentCommand
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<ReserveInventoryCommand>();
    }

    public override async Task CompensateAsync(ReserveInventoryCommand message, CancellationToken cancellationToken = default)
    {
        // Compensation logic: release reserved inventory
        Context.Data.InventoryCompensated = true;
        await Context.CompensateAndBubbleUp<ReserveInventoryCommand>(cancellationToken);
    }
}