using Lycia.Saga.Handlers;
using Microsoft.Extensions.Logging;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class ReserveStockSagaHandler : ReactiveSagaHandler<OrderCreatedEvent>
{
    private readonly ILogger<ReserveStockSagaHandler> logger;
    public ReserveStockSagaHandler(ILogger<ReserveStockSagaHandler> _logger)
    {
        logger = _logger;
    }

    public override async Task HandleAsync(OrderCreatedEvent orderCreatedEvent)
    {
        if (orderCreatedEvent == null)
        {
            logger.LogError("OrderCreatedEvent is null");
            throw new ArgumentNullException(nameof(orderCreatedEvent));
        }

        //Insert into db

        var reserveStockEvent = StockReservedEvent.Create
        (
            orderCreatedEvent.OrderId,
            orderCreatedEvent.CustomerId,
            orderCreatedEvent.Items);

        await Context.PublishWithTracking(reserveStockEvent).ThenMarkAsComplete();
    }

    public override async Task CompensateAsync(OrderCreatedEvent message)
    {
        try
        {
            if (message.OrderId == Guid.Empty)
            {
                throw new InvalidOperationException("Total price must be greater than zero for compensation.");
            }

            logger.LogInformation("Compensating for failed stock reservation. OrderId: {OrderId}", message.OrderId);
            await Context.MarkAsCompensated<OrderCreatedEvent>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compensation failed");
            await Context.MarkAsCompensationFailed<OrderCreatedEvent>();
        }
    }
}