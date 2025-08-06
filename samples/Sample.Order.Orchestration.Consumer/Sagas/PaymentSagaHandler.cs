using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;
using Sample.Shared.Services;

namespace Sample.Order.Orchestration.Consumer.Sagas;

public class PaymentSagaHandler :
    CoordinatedResponsiveSagaHandler<ProcessPaymentCommand, PaymentSucceededResponse, CreateOrderSagaData>
{
    public override async Task HandleAsync(ProcessPaymentCommand message)
    {
        // Simulate payment process
        var paymentSucceeded = PaymentService.SimulatePayment();

        if (!paymentSucceeded)
        {
            // Payment failed, compensation chain is initiated
            await Context.MarkAsFailed<ProcessPaymentCommand>();
            return;
        }

        // Pivot step: no compensation after this point, only retry
        Context.Data.PaymentIrreversible = true;

        // Continue
        await Context.Publish(new PaymentSucceededResponse
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        });
        await Context.MarkAsComplete<ProcessPaymentCommand>();
    }
}