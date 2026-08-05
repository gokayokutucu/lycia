// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;
using Sample.Shared.Services;

namespace Sample.Order.Orchestration.Consumer.Sagas;

public class ShippingSagaHandler :
    CoordinatedSagaHandler<ShipOrderCommand, CreateOrderSagaData>
{
    public override async Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
    {
        if (!ShippingService.TryShip(message.OrderId, SampleScenario.FailShipping))
        {
            await Context.MarkAsFailed<ShipOrderCommand>(cancellationToken);
            return;
        }

        await Context.Respond(message, new OrderShippedResponse
        {
            OrderId = message.OrderId
        }, cancellationToken);
        await Context.MarkAsComplete<ShipOrderCommand>();
    }

    public override Task CompensateAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
    {
        Context.Data.ShippingCompensated = true; // Sample flag to indicate compensation
        return Context.CompensateAndBubbleUp<ShipOrderCommand>(cancellationToken);
    }
}
