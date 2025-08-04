using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Sequential.Consumer_.Sagas;

public class PaymentSagaHandler :
    CoordinatedSagaHandler<ProcessPaymentCommand, CreateOrderSagaData>
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
    
    public override async Task CompensateAsync(ProcessPaymentCommand message)
    {
        // No business compensation required, but need to bubble up
        await Context.CompensateAndBubbleUp<ProcessPaymentCommand>();
    }
}

public static class PaymentService
{
    public static bool SimulatePayment(bool paymentSucceeded = true)
    {
        return paymentSucceeded;
    }
}