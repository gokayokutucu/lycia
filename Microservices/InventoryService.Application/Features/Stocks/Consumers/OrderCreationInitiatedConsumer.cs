using MediatR;
using Lycia.Saga.Abstractions;
using Sample.Shared.Messages.Events;
using InventoryService.Application.Contracts.Persistence; // For IInventoryRepository
using InventoryService.Application.Features.Stocks.Notifications; // For MediatR Wrappers
// using InventoryService.Domain.Entities; // For Stock entity, if directly used
using System.Threading;
using System.Threading.Tasks;
using System; // For Console.WriteLine

namespace InventoryService.Application.Features.Stocks.Consumers
{
    public class OrderCreationInitiatedConsumer : INotificationHandler<OrderCreationInitiatedMediatRNotification>
    {
        private readonly IEventBus _eventBus;
        private readonly IInventoryRepository _inventoryRepository;

        public OrderCreationInitiatedConsumer(IEventBus eventBus, IInventoryRepository inventoryRepository)
        {
            _eventBus = eventBus;
            _inventoryRepository = inventoryRepository;
        }

        public async Task Handle(OrderCreationInitiatedMediatRNotification notificationWrapper, CancellationToken cancellationToken)
        {
            var notification = notificationWrapper.OriginalEvent; // Get the original event
            Console.WriteLine($"InventoryService: Handling OrderCreationInitiatedEvent (via MediatR wrapper) for OrderId {notification.OrderId}, ProductId {notification.ProductId}, Quantity {notification.Quantity}");

            // TODO: Replace mock logic with actual stock check using _inventoryRepository
            // This would require IInventoryRepository to have a method like GetByProductIdAsync(string productId)
            // For example:
            // Guid productGuid;
            // if (!Guid.TryParse(notification.ProductId, out productGuid))
            // {
            //     Console.WriteLine($"InventoryService: Invalid ProductId format {notification.ProductId}");
            //     // Potentially publish a specific error event or handle as needed
            //     return;
            // }
            // var stock = await _inventoryRepository.GetByProductIdAsync(productGuid, cancellationToken);
            // bool isAvailable = stock != null && stock.Quantity >= notification.Quantity;
            // int currentStockQuantity = stock?.Quantity ?? 0;

            // For now, simulate stock check
            bool isAvailable = notification.Quantity < 50;
            int currentStockQuantity = isAvailable ? 100 : (notification.Quantity - 10); // Mock available quantity
            string reasonIfNotAvailable = string.Empty;

            if (isAvailable)
            {
                // Optional: Placeholder for actual stock deduction logic
                // if (stock != null)
                // {
                //     var updatedStock = stock with { Quantity = stock.Quantity - notification.Quantity };
                //     await _inventoryRepository.UpdateAsync(updatedStock, cancellationToken);
                // }

                var stockAvailableEvent = new StockAvailableEvent
                {
                    OrderId = notification.OrderId,
                    ProductId = notification.ProductId,
                    Quantity = notification.Quantity,
                    SagaId = notification.SagaId // Propagate SagaId
                };
                // Lycia's IEventBus.Publish is synchronous in the provided InMemoryEventBus
                _eventBus.Publish(stockAvailableEvent, notification.SagaId); // Pass SagaId to Publish method
                Console.WriteLine($"InventoryService: Published StockAvailableEvent for OrderId {notification.OrderId}, SagaId {notification.SagaId}");
            }
            else
            {
                reasonIfNotAvailable = $"Insufficient stock for ProductId {notification.ProductId}. Required: {notification.Quantity}, Available: {currentStockQuantity}.";
                var stockUnavailableEvent = new StockUnavailableEvent
                {
                    OrderId = notification.OrderId,
                    ProductId = notification.ProductId,
                    Quantity = notification.Quantity,
                    Reason = reasonIfNotAvailable,
                    SagaId = notification.SagaId // Propagate SagaId
                };
                _eventBus.Publish(stockUnavailableEvent, notification.SagaId); // Pass SagaId to Publish method
                Console.WriteLine($"InventoryService: Published StockUnavailableEvent for OrderId {notification.OrderId}, SagaId {notification.SagaId}. Reason: {reasonIfNotAvailable}");
            }
            // Ensure awaiting async operations if any were actually performed, e.g. from a real PublishAsync
            // For now, if _eventBus.Publish is synchronous, no top-level await is strictly needed here,
            // but keeping async Task for future compatibility.
            await Task.CompletedTask; // Placeholder if Publish becomes truly async
        }
    }
}
