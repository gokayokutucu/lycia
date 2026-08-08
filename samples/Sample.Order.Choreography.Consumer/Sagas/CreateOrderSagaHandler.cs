// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Messaging.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;

namespace Sample.Order.Choreography.Consumer.Sagas;

public sealed class CreateOrderSagaHandler :
    StartReactiveSagaHandler<CreateOrderCommand>,
    ISagaCompensationHandler<PaymentFailedEvent>,
    ISagaCompensationHandler<OrderShippingFailedEvent>
{
    protected override bool EnforceIdempotency => true;

    public override async Task HandleStartAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        // Create the order in Pending state.

        await Context.Publish(
            new OrderCreatedEvent
            {
                OrderId = command.OrderId
            },
            cancellationToken);

        await Context.MarkAsComplete<CreateOrderCommand>();
    }

    public override async Task CompensateStartAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Cancel or remove the initially created order.

            await Context.MarkAsCompensated<CreateOrderCommand>();
        }
        catch
        {
            await Context.MarkAsCompensationFailed<CreateOrderCommand>();

            throw;
        }
    }

    public async Task CompensateAsync(
        PaymentFailedEvent failed,
        CancellationToken cancellationToken = default)
    {
        if (await Context.IsAlreadyCompleted<PaymentFailedEvent>())
        {
            return;
        }

        // Mark the order as cancelled because payment failed.

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

        // Mark the order as cancelled because shipping failed.

        await Context.MarkAsCompensated<OrderShippingFailedEvent>();
    }
}