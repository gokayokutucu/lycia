using Lycia.Saga.Handlers;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Events;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class ReserveStockSagaHandler(ILogger<ReserveStockSagaHandler> logger, IMapper mapper, IStockRepository stockRepository)
    : ReactiveSagaHandler<OrderCreatedSagaEvent>
{
    public override async Task HandleAsync(OrderCreatedSagaEvent orderCreatedEvent)
    {
        var isAvailable = await stockRepository.IsStockAvailableAsync(orderCreatedEvent.ProductId, orderCreatedEvent.Quantity);
        if(!isAvailable)
            throw new InvalidOperationException($"Insufficient stock for ProductId: {orderCreatedEvent.ProductId}, Quantity: {orderCreatedEvent.Quantity}");

        await Context.PublishWithTracking(reserveStockEvent).ThenMarkAsComplete();
    }

    public override async Task CompensateAsync(OrderCreatedSagaEvent message)
    {
        try
        {
            if (message.OrderId == Guid.Empty)
            {
                throw new InvalidOperationException("Total price must be greater than zero for compensation.");
            }

            logger.LogInformation("Compensating for failed stock reservation. OrderId: {OrderId}", message.OrderId);
            await Context.MarkAsCompensated<OrderCreatedSagaEvent>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compensation failed");
            await Context.MarkAsCompensationFailed<OrderCreatedSagaEvent>();
        }
    }
}