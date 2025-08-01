using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Sequential.Consumer_.Sagas;

public class ShippingSagaHandler : 
    CoordinatedSagaHandler<OrderCreatedEvent, ShippedOrderResponse, CreateOrderSagaData>
{
    public override async Task HandleAsync(OrderCreatedEvent command)
    {
        try
        {
            // Simulated logic
            const bool stockAvailable = true; // Simulate failure

            if (!stockAvailable)
            {
                await Context.MarkAsFailed<OrderCreatedEvent>();
                return;
            }

            await Context.MarkAsComplete<OrderCreatedEvent>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 Shipping failed: {ex.Message}");
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
    }
}