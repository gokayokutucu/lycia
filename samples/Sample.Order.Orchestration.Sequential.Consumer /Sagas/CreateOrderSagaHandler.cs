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
    StartCoordinatedSagaHandler<CreateOrderCommand, CreateOrderSagaData>
{
    /// <summary>
    /// For test purposes, we can check if the compensation was called.
    /// </summary>
    public bool CompensateCalled { get; private set; }
    
    public override async Task HandleStartAsync(CreateOrderCommand command)
    {
        await Context.SendWithTracking(new ReserveInventoryCommand
        {
            OrderId = command.OrderId,
        })
        .ThenMarkAsComplete();
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
}