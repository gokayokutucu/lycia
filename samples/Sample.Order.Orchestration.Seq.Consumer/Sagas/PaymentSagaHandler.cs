// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;
using Sample.Shared.SagaStates;
using Sample.Shared.Services;

namespace Sample.Order.Orchestration.Seq.Consumer.Sagas;

public class PaymentSagaHandler :
    CoordinatedSagaHandler<ProcessPaymentCommand, CreateOrderSagaData>
{
    public override async Task HandleAsync(ProcessPaymentCommand message, CancellationToken cancellationToken = default)
    {
        // Simulate payment process
        var paymentSucceeded = PaymentService.SimulatePayment(false);

        if (!paymentSucceeded)
        {
            // Payment failed, compensation chain is initiated
            await Context.MarkAsFailed<ProcessPaymentCommand>(cancellationToken);
            return;
        }

        // Pivot step: no compensation after this point, only retry
        Context.Data.PaymentIrreversible = true;

        // Continue
        await Context.Publish(new PaymentProcessedEvent
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<ProcessPaymentCommand>();
    }
    
    public override async Task CompensateAsync(ProcessPaymentCommand message, CancellationToken cancellationToken = default)
    {
        // No business compensation required, but need to bubble up
        await Context.CompensateAndBubbleUp<ProcessPaymentCommand>(cancellationToken);
    }
}