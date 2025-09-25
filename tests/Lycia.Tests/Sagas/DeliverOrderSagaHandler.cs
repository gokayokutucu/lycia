// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

public class DeliverOrderSagaHandler : CoordinatedSagaHandler<OrderShippedEvent, CreateOrderSagaData>
{
    public override async Task HandleAsync(OrderShippedEvent command, CancellationToken cancellationToken = default)
    {
        // Simulate delivery logic
        await Context.PublishWithTracking(new OrderDeliveredEvent
        {
            OrderId = command.OrderId
        }, cancellationToken)
            .ThenMarkAsComplete();
    }
}