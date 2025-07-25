using Lycia.Saga.Handlers;
using Microsoft.Extensions.Logging;
using Sample_Net21.Shared.Messages.Events;
using System;
using System.Threading.Tasks;

namespace Sample_Core31.Order.Choreography.Api.Sagas
{
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

            logger.LogInformation("ReserveStock Completed for OrderId: {OrderId}", orderCreatedEvent.OrderId);
            await Context.PublishWithTracking(reserveStockEvent).ThenMarkAsComplete();
        }

        public override async Task CompensateAsync(OrderCreatedEvent message)
        {
            //STOCK
            try
            {
                logger.LogInformation("ReserveStock Compensated for OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensated<OrderCreatedEvent>();
            }
            catch (Exception ex)
            {
                logger.LogInformation("ReserveStock Compensation Failed for OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensationFailed<OrderCreatedEvent>();
            }
        }
    }
}