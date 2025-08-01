using Lycia.Messaging;
using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Sequential.Consumer_.Sagas;

/// <summary>
/// Handles the start of the order process in a reactive saga flow and emits an OrderCreatedEvent.
/// </summary>
public class CreateOrderSagaHandler :
    StartCoordinatedSagaHandler<CreateOrderCommand, OrderCreatedResponse, CreateOrderSagaData>
{
    /// <summary>
    /// For test purposes, we can check if the compensation was called.
    /// </summary>
    public bool CompensateCalled { get; private set; }
    
    public override async Task HandleStartAsync(CreateOrderCommand command)
    {
        await Context.Publish(new OrderCreatedEvent
        {
            OrderId = command.OrderId,
        });
    }

    public override async Task CompensateStartAsync(CreateOrderCommand message)
    {
        try
        {
            CompensateCalled = true;
            // Compensation logic
            await Context.MarkAsCompensated<CreateOrderCommand>();
        }
        catch (Exception ex)
        {
            // Log, notify, halt chain, etc.
            Console.WriteLine($"❌ Compensation failed: {ex.Message}");
            
            await Context.MarkAsCompensationFailed<CreateOrderCommand>();
            // Optionally: rethrow or store for manual retry
            throw; // Or suppress and log for retry system
        }
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
        return Context.MarkAsFailed<CreateOrderCommand>();
    }
}