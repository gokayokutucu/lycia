// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Consumer.Sagas;

public class InventorySagaHandler : 
    CoordinatedSagaHandler<ReserveInventoryCommand, CreateOrderSagaData>
{
    public override async Task HandleAsync(ReserveInventoryCommand message, CancellationToken cancellationToken = default)
    {
        await Context
            .RespondWithTracking(message, new InventoryReservedResponse
            {
                OrderId = message.OrderId
            })
            .ThenMarkAsComplete<ReserveInventoryCommand>(cancellationToken);
    }

    public override Task CompensateAsync(ReserveInventoryCommand message, CancellationToken cancellationToken = default)
    {
        Context.Data.InventoryCompensated = true;
        return Context.CompensateAndBubbleUp<ReserveInventoryCommand>(cancellationToken);
    }
}
