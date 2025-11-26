// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Messaging;
using Lycia.Saga;
using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Messaging.Handlers;
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
    public override async Task HandleStartAsync(CreateOrderCommand message, CancellationToken cancellationToken = default)
    {
        // Persist order in the database, perform initial business logic
        await Context.Publish(new OrderCreatedResponse
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<CreateOrderCommand>();
    }

    public override async Task HandleSuccessResponseAsync(OrderCreatedResponse response, CancellationToken cancellationToken = default)
    {
        // Order created, reserve inventory
        await Context.Send(new ReserveInventoryCommand
        {
            OrderId = response.OrderId,
            ParentMessageId = response.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<OrderCreatedResponse>();
    }

    public override Task HandleFailResponseAsync(OrderCreatedResponse response, FailResponse fail, CancellationToken cancellationToken = default)
    {
        // Order could not be created, mark the saga as failed, log, or start compensation
        return Task.CompletedTask;
    }

    public async Task HandleSuccessResponseAsync(InventoryReservedResponse response, CancellationToken cancellationToken = default)
    {
        // Inventory reserved, start payment process
        await Context.Send(new ProcessPaymentCommand
        {
            OrderId = response.OrderId,
            ParentMessageId = response.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<InventoryReservedResponse>();
    }

    public Task HandleFailResponseAsync(InventoryReservedResponse response, FailResponse fail, CancellationToken cancellationToken = default)
    {
        // Inventory reservation failed, cancel the order or log
        return Task.CompletedTask;
    }

    public async Task HandleSuccessResponseAsync(PaymentSucceededResponse response, CancellationToken cancellationToken = default)
    {
        // Payment complete, start shipping process
        await Context.Send(new ShipOrderCommand
        {
            OrderId = response.OrderId,
            ParentMessageId = response.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<PaymentSucceededResponse>();
    }

    public Task HandleFailResponseAsync(PaymentSucceededResponse response, FailResponse fail, CancellationToken cancellationToken = default)
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

    public async Task HandleSuccessResponseAsync(OrderShippedResponse response, CancellationToken cancellationToken = default)
    {
        // All steps completed
        // Here you can mark the saga as completed (DB update, event publish, etc.)
        // Example:
        Context.Data.IsCompleted = true;
        await Context.MarkAsComplete<OrderShippedResponse>();
    }

    public Task HandleFailResponseAsync(OrderShippedResponse response, FailResponse fail, CancellationToken cancellationToken = default)
    {
        // Shipping failed, notify the customer or start compensation
        return Task.CompletedTask;
    }
}