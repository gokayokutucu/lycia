using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Consumer.Sagas;

public class PaymentSagaHandler :
    CoordinatedResponsiveSagaHandler<ProcessPaymentCommand, PaymentSucceededResponse, CreateOrderSagaData>
{
    public override async Task HandleAsync(ProcessPaymentCommand message)
    {
        await Context.Publish(new PaymentSucceededResponse
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        });
        //Pivot point
        await Context.MarkAsComplete<ProcessPaymentCommand>();
    }
}