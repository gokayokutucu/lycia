using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Sagas;
using System.Threading.Tasks;

namespace OrderService.Application.Features.Orders.Sagas
{
    public class OrderStockCheckSagaHandler :
        ISagaStartHandler<OrderCreationInitiatedEvent, OrderStockCheckSagaData>,
        ISagaHandler<StockAvailableEvent, OrderStockCheckSagaData>,
        ISagaHandler<StockUnavailableEvent, OrderStockCheckSagaData>
    {
        // Initialization for ISagaStartHandler<OrderCreationInitiatedEvent, OrderStockCheckSagaData>
        public void Initialize(ISagaContext<OrderCreationInitiatedEvent, OrderStockCheckSagaData> context)
        {
        }

        // Initialization for ISagaHandler<StockAvailableEvent, OrderStockCheckSagaData>
        public void Initialize(ISagaContext<StockAvailableEvent, OrderStockCheckSagaData> context)
        {
        }

        // Initialization for ISagaHandler<StockUnavailableEvent, OrderStockCheckSagaData>
        public void Initialize(ISagaContext<StockUnavailableEvent, OrderStockCheckSagaData> context)
        {
        }

        public async Task HandleStartAsync(OrderCreationInitiatedEvent command, ISagaContext<OrderCreationInitiatedEvent, OrderStockCheckSagaData> context)
        {
            // Initialize SagaData
            context.Data.OrderId = command.OrderId;
            context.Data.UserId = command.UserId;
            context.Data.ProductId = command.ProductId;
            context.Data.Quantity = command.Quantity;
            context.Data.TotalPrice = command.TotalPrice;

            // No explicit event publishing here if InventoryService listens to OrderCreationInitiatedEvent.
            // If StockCheckRequestedEvent were used, it would be published here:
            // await context.Publish(new StockCheckRequestedEvent { ... });

            // The saga is now waiting for StockAvailableEvent or StockUnavailableEvent
        }

        public async Task HandleAsync(StockAvailableEvent @event, ISagaContext<StockAvailableEvent, OrderStockCheckSagaData> context)
        {
            // Ensure the event correlates to the current saga instance
            if (context.Data.OrderId != @event.OrderId)
            {
                // This event is not for this saga instance, ignore it.
                // Lycia might handle this correlation automatically based on SagaId,
                // but an explicit check can be good.
                return;
            }

            // Publish OrderConfirmedEvent
            await context.Publish(new OrderConfirmedEvent { OrderId = @event.OrderId });

            // Mark the saga as complete
            await context.MarkAsComplete<StockAvailableEvent>();
        }

        public async Task HandleAsync(StockUnavailableEvent @event, ISagaContext<StockUnavailableEvent, OrderStockCheckSagaData> context)
        {
            // Ensure the event correlates to the current saga instance
            if (context.Data.OrderId != @event.OrderId)
            {
                return;
            }

            // Publish OrderCreationFailedEvent
            await context.Publish(new OrderCreationFailedEvent
            {
                OrderId = @event.OrderId,
                Reason = @event.Reason
            });

            // Mark the saga as compensated or failed
            // Choose MarkAsCompensated if you have explicit compensation steps.
            // Choose MarkAsFailed if it's a terminal failure for this path.
            await context.MarkAsFailed<StockUnavailableEvent>(); // Or context.MarkAsCompensated();
        }
    }
}
