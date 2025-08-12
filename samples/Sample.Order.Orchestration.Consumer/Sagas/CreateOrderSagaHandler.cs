using Lycia.Messaging;
using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Consumer.Sagas;

public class CreateOrderSagaHandler :
    StartCoordinatedResponsiveSagaHandler<CreateOrderCommand, OrderCreatedResponse, CreateOrderSagaData>,
    IResponseSagaHandler<InventoryReservedResponse>,
    IResponseSagaHandler<PaymentSucceededResponse>,
    IResponseSagaHandler<OrderShippedResponse>
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

    public override async Task HandleSuccessResponseAsync(OrderCreatedResponse response)
    {
        // Order created, reserve inventory
        await Context.Send(new ReserveInventoryCommand
        {
            OrderId = response.OrderId,
            ParentMessageId = response.MessageId
        });
        await Context.MarkAsComplete<OrderCreatedResponse>();
    }

    public override Task HandleFailResponseAsync(OrderCreatedResponse response, FailResponse fail)
    {
        // Order could not be created, mark the saga as failed, log, or start compensation
        return Task.CompletedTask;
    }

    public async Task HandleSuccessResponseAsync(InventoryReservedResponse response)
    {
        // Inventory reserved, start payment process
        await Context.Send(new ProcessPaymentCommand
        {
            OrderId = response.OrderId,
            ParentMessageId = response.MessageId
        });
        await Context.MarkAsComplete<InventoryReservedResponse>();
    }

    public Task HandleFailResponseAsync(InventoryReservedResponse response, FailResponse fail)
    {
        // Inventory reservation failed, cancel the order or log
        return Task.CompletedTask;
    }

    public async Task HandleSuccessResponseAsync(PaymentSucceededResponse response)
    {
        // Payment complete, start shipping process
        await Context.Send(new ShipOrderCommand
        {
            OrderId = response.OrderId,
            ParentMessageId = response.MessageId
        });
        await Context.MarkAsComplete<PaymentSucceededResponse>();
    }

    public Task HandleFailResponseAsync(PaymentSucceededResponse response, FailResponse fail)
    {
        /* Payment failed, revert reservation, or cancel the order */
        // If payment is irreversible, we cannot compensate:
        if (Context.Data.PaymentIrreversible)
        {
            // Don't compensate, just log or notify
            Console.WriteLine("Payment irreversible! Compensation skipped.");
            return Task.CompletedTask;
        }

        // Trigger compensation chains for payment(Call the InventorySagaHandler CompensateAsync method)
        return Task.CompletedTask;
    }

    public async Task HandleSuccessResponseAsync(OrderShippedResponse response)
    {
        // All steps completed
        // Here you can mark the saga as completed (DB update, event publish, etc.)
        // Example:
        Context.Data.IsCompleted = true;
        await Context.MarkAsComplete<OrderShippedResponse>();
    }

    public Task HandleFailResponseAsync(OrderShippedResponse response, FailResponse fail)
    {
        // Shipping failed, notify the customer or start compensation
        return Task.CompletedTask;
    }
}