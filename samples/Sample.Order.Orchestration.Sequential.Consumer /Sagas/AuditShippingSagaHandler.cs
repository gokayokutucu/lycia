using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Sequential.Consumer_.Sagas;

public class AuditShippingSagaHandler : 
    CoordinatedSagaHandler<OrderCreatedEvent, OrderAuditedResponse, CreateOrderSagaData>
{
    public override async Task HandleAsync(OrderCreatedEvent message)
    {
        try
        {
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
        catch (Exception e)
        {
            Console.WriteLine($"🚨 Audit failed: {e.Message}");
            await Context.MarkAsFailed<OrderCreatedEvent>();
        }
    }

    public override async Task CompensateAsync(OrderCreatedEvent message)
    {
        try
        {
            await Context.CompensateAndBubbleUp<OrderCreatedEvent>();
        }
        catch (Exception e)
        {
            await Context.MarkAsCompensationFailed<OrderCreatedEvent>();
            throw;
        }
    }
}