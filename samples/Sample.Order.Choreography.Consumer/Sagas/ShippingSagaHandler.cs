// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.Services;

namespace Sample.Order.Choreography.Consumer.Sagas;

public sealed class ShippingSagaHandler :
    ReactiveSagaHandler<PaymentSucceededEvent>
{
    protected override bool EnforceIdempotency => true;

    public override async Task HandleAsync(
        PaymentSucceededEvent message,
        CancellationToken cancellationToken = default)
    {
        var shipped = ShippingService.TryShip(message.OrderId);

        if (!shipped)
        {
            await Context.Publish(
                new OrderShippingFailedEvent
                {
                    OrderId = message.OrderId
                },
                cancellationToken);

            await Context.MarkAsFailed<PaymentSucceededEvent>(
                cancellationToken);

            return;
        }

        await Context.Publish(
            new OrderShippedEvent
            {
                OrderId = message.OrderId
            },
            cancellationToken);

        await Context.MarkAsComplete<PaymentSucceededEvent>();
    }
}