// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Messaging.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.Services;

namespace Sample.Order.Choreography.Consumer.Sagas;

public sealed class PaymentSagaHandler :
    ReactiveSagaHandler<InventoryReservedEvent>,
    ISagaCompensationHandler<OrderShippingFailedEvent>
{
    protected override bool EnforceIdempotency => true;

    public override async Task HandleAsync(
        InventoryReservedEvent message,
        CancellationToken cancellationToken = default)
    {
        var succeeded =
            PaymentService.SimulatePayment(SampleScenario.FailPayment);

        if (!succeeded)
        {
            await Context.Publish(
                new PaymentFailedEvent
                {
                    OrderId = message.OrderId
                },
                cancellationToken);

            await Context.MarkAsFailed<InventoryReservedEvent>(
                cancellationToken);

            return;
        }

        await Context.Publish(
            new PaymentSucceededEvent
            {
                OrderId = message.OrderId
            },
            cancellationToken);

        await Context.MarkAsComplete<InventoryReservedEvent>();
    }

    public async Task CompensateAsync(
        OrderShippingFailedEvent failed,
        CancellationToken cancellationToken = default)
    {
        if (await Context.IsAlreadyCompleted<OrderShippingFailedEvent>())
        {
            return;
        }

        // Refund the successful payment idempotently.
        PaymentService.Refund(failed.OrderId);

        await Context.MarkAsCompensated<OrderShippingFailedEvent>();
    }
}