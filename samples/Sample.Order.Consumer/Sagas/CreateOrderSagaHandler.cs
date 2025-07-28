using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;

namespace Sample.Order.Consumer.Sagas;

/// <summary>
/// Handles the start of the order process in a reactive saga flow and emits an OrderCreatedEvent.
/// </summary>
public class CreateOrderSagaHandler :
    StartReactiveSagaHandler<CreateOrderCommand>
{
    /// <summary>
    /// For test purposes, we can check if the compensation was called.
    /// </summary>
    public bool CompensateCalled { get; private set; }
    
    public override async Task HandleStartAsync(CreateOrderCommand command)
    {
        await Context.PublishWithTracking(new OrderCreatedEvent
        {
            OrderId = command.OrderId,
        }).ThenMarkAsComplete();
        //await Context.MarkAsComplete<CreateOrderCommand>();
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