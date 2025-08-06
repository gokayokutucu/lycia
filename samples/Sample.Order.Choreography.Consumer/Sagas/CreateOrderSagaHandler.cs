using Lycia.Messaging;
using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Choreography.Consumer.Sagas;

public class CreateOrderSagaHandler :
    StartReactiveSagaHandler<CreateOrderCommand>,
    ISagaCompensationHandler<PaymentFailedEvent>,
    ISagaCompensationHandler<OrderShippingFailedEvent>
{
    public override async Task HandleStartAsync(CreateOrderCommand message)
    {
        // Persist order in the database, perform initial business logic
        await Context.Publish(new OrderCreatedResponse
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        });
        await Context.MarkAsComplete<CreateOrderCommand>();
    }


    public Task CompensateAsync(PaymentFailedEvent message)
    {
        throw new NotImplementedException();
    }

    public Task CompensateAsync(OrderShippingFailedEvent message)
    {
        throw new NotImplementedException();
    }
}