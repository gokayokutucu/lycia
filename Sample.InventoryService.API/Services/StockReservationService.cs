using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions; // For IMessagePublisher
using Lycia.Saga; // For StepStatus
using Lycia.Saga.Abstractions; // For ISagaStore
using Sample.InventoryService.API.Dtos;
using Sample.InventoryService.API.Events;
using Sample.InventoryService.API.Models; // For InventorySagaData
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sample.InventoryService.API.Services
{
    public class StockReservationService
    {
        private readonly IMessagePublisher _messagePublisher;
        private readonly ISagaStore _sagaStore;
        private readonly ILogger<StockReservationService> _logger;

        // Simulate an in-memory stock inventory
        private static readonly Dictionary<string, int> _inventory = new Dictionary<string, int>()
        {
            {"product123", 100},
            {"product456", 50},
            {"product789", 0} // Product with no stock
        };

        public StockReservationService(
            IMessagePublisher messagePublisher, 
            ISagaStore sagaStore, // Injected ISagaStore
            ILogger<StockReservationService>? logger)
        {
            _messagePublisher = messagePublisher ?? throw new ArgumentNullException(nameof(messagePublisher));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore)); // Store injected ISagaStore
            _logger = logger ?? NullLogger<StockReservationService>.Instance;
        }

        public async Task<bool> ReserveStockAsync(Guid sagaId, Guid orderId, List<OrderItemDto> itemsToReserve)
        {
            _logger.LogInformation("Attempting stock reservation for OrderId: {OrderId}, SagaId: {SagaId}", orderId, sagaId);
            
            // Log "Processing" step for this service's core logic
            // This step is more granular for the service itself, OrderCreatedEventHandler logs its own processing step
            await _sagaStore.LogStepAsync(sagaId, typeof(StockReservationService), StepStatus.Processing, new { OrderId = orderId, Items = itemsToReserve });

            var reservedItemsList = new List<ReservedItemDto>();
            bool allItemsSuccessfullyReserved = true;
            string failureReason = string.Empty;

            foreach (var item in itemsToReserve)
            {
                if (_inventory.TryGetValue(item.ProductId, out int currentStock) && currentStock >= item.Quantity)
                {
                    reservedItemsList.Add(new ReservedItemDto(item.ProductId, item.Quantity));
                    _logger.LogInformation("Product {ProductId} can be reserved in quantity {Quantity} for OrderId {OrderId}.", item.ProductId, item.Quantity, orderId);
                }
                else
                {
                    allItemsSuccessfullyReserved = false;
                    failureReason = $"Insufficient stock for ProductId: {item.ProductId}. Requested: {item.Quantity}, Available: {( _inventory.TryGetValue(item.ProductId, out int stock) ? stock : 0)}";
                    _logger.LogWarning("Stock reservation failed for ProductId {ProductId} for OrderId {OrderId}. Reason: {FailureReason}", item.ProductId, orderId, failureReason);
                    break; 
                }
            }

            SagaData? sagaData; // Declare here to use in both branches

            if (allItemsSuccessfullyReserved)
            {
                foreach (var item in itemsToReserve)
                {
                     _inventory[item.ProductId] -= item.Quantity; 
                }
                _logger.LogInformation("All items successfully reserved for OrderId: {OrderId}. Committing stock changes.", orderId);

                var stockReservedEvent = new StockReservedEvent(sagaId, orderId, reservedItemsList);
                try
                {
                    _logger.LogInformation("Publishing StockReservedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                        stockReservedEvent.MessageId, orderId, sagaId);
                    await _messagePublisher.PublishAsync("saga_events_exchange", "order.stock.reserved", stockReservedEvent);
                    _logger.LogInformation("Successfully published StockReservedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                        stockReservedEvent.MessageId, orderId, sagaId);
                    
                    await _sagaStore.LogStepAsync(sagaId, typeof(StockReservedEvent), StepStatus.Completed, new { EventMessageId = stockReservedEvent.MessageId, stockReservedEvent.OrderId, stockReservedEvent.SagaId, ItemsCount = stockReservedEvent.ItemsReserved.Count });
                    
                    sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                    if (sagaData != null)
                    {
                        sagaData.Extras["OverallStatus"] = "StockReserved_AwaitingPayment";
                        sagaData.Extras["InventoryServiceStatus"] = "Completed_StockReserved";
                        sagaData.Extras[$"ReservedItems_{orderId}"] = System.Text.Json.JsonSerializer.Serialize(reservedItemsList); // Store reserved items
                        sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                        await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                    }
                    else
                    {
                        _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} after successfully reserving stock. This is highly unexpected.", sagaId);
                        // Create and save a new SagaData as a fallback, though this indicates a problem.
                        sagaData = new InventorySagaData { Extras = {
                            ["OrderId"] = orderId.ToString(),
                            ["SagaType"] = "OrderPlacementSaga",
                            ["OverallStatus"] = "StockReserved_AwaitingPayment_Recovered",
                            ["InventoryServiceStatus"] = "Completed_StockReserved",
                            [$"ReservedItems_{orderId}"] = System.Text.Json.JsonSerializer.Serialize(reservedItemsList), // Store reserved items
                            ["Error"] = "SagaData was missing, recovered in StockReservationService after success.",
                            ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
                        }};
                        await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish StockReservedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                        stockReservedEvent.MessageId, orderId, sagaId);
                    await _sagaStore.LogStepAsync(sagaId, typeof(StockReservedEvent), StepStatus.Failed, new { Error = ex.Message, Note = "Publishing StockReservedEvent failed", EventMessageId = stockReservedEvent.MessageId });
                    // Update SagaData to reflect publishing failure
                    sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                    if (sagaData != null) {
                        sagaData.Extras["InventoryServiceStatus"] = "Failed_PublishStockReservedEvent";
                        sagaData.Extras["ErrorDetails_StockReservationService"] = $"Publishing StockReservedEvent failed: {ex.Message}";
                        sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                        await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                    }
                    return false; 
                }
            }
            else
            {
                var stockReservationFailedEvent = new StockReservationFailedEvent(sagaId, orderId, failureReason);
                try
                {
                    _logger.LogInformation("Publishing StockReservationFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {Reason}", 
                        stockReservationFailedEvent.MessageId, orderId, sagaId, failureReason);
                    await _messagePublisher.PublishAsync("saga_events_exchange", "order.stock.reservation_failed", stockReservationFailedEvent);
                    _logger.LogInformation("Successfully published StockReservationFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                        stockReservationFailedEvent.MessageId, orderId, sagaId);

                    await _sagaStore.LogStepAsync(sagaId, typeof(StockReservationFailedEvent), StepStatus.Completed, new { EventMessageId = stockReservationFailedEvent.MessageId, stockReservationFailedEvent.OrderId, stockReservationFailedEvent.SagaId, stockReservationFailedEvent.Reason });

                    sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                    if (sagaData != null)
                    {
                        sagaData.Extras["OverallStatus"] = "Failed_StockReservation";
                        sagaData.Extras["InventoryServiceStatus"] = "Failed_StockUnavailable";
                        sagaData.Extras["ReasonForFailure_Inventory"] = failureReason;
                        sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                        await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                    }
                     else
                    {
                        _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} after stock reservation failed. This is highly unexpected.", sagaId);
                         sagaData = new InventorySagaData { Extras = {
                            ["OrderId"] = orderId.ToString(),
                            ["SagaType"] = "OrderPlacementSaga",
                            ["OverallStatus"] = "Failed_StockReservation_Recovered",
                            ["InventoryServiceStatus"] = "Failed_StockUnavailable",
                            ["ReasonForFailure_Inventory"] = failureReason,
                            ["Error"] = "SagaData was missing, recovered in StockReservationService after failure.",
                            ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
                        }};
                        await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish StockReservationFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                        stockReservationFailedEvent.MessageId, orderId, sagaId);
                    await _sagaStore.LogStepAsync(sagaId, typeof(StockReservationFailedEvent), StepStatus.Failed, new { Error = ex.Message, Note = "Publishing StockReservationFailedEvent failed", EventMessageId = stockReservationFailedEvent.MessageId });
                     sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                    if (sagaData != null) {
                        sagaData.Extras["InventoryServiceStatus"] = "Failed_PublishStockReservationFailedEvent";
                        sagaData.Extras["ErrorDetails_StockReservationService"] = $"Publishing StockReservationFailedEvent failed: {ex.Message}";
                        sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                        await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                    }
                }
                return false; 
            }
        }

        public async Task ReleaseStockAsync(Guid sagaId, Guid orderId, string reasonForRelease)
        {
            _logger.LogInformation("Attempting to release stock for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {Reason}", orderId, sagaId, reasonForRelease);
            await _sagaStore.LogStepAsync(sagaId, typeof(StockReservationService), StepStatus.Compensating, new { OrderId = orderId, Action = "ReleaseStock", Reason = reasonForRelease });

            var itemsToReleaseDetails = new List<ReleasedItemDto>();
            SagaData? sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);

            if (sagaData != null && sagaData.Extras.TryGetValue($"ReservedItems_{orderId}", out var reservedItemsJsonObj) && reservedItemsJsonObj is string reservedItemsJson)
            {
                try
                {
                    var reservedItems = System.Text.Json.JsonSerializer.Deserialize<List<OrderItemDto>>(reservedItemsJson);
                    if (reservedItems != null && reservedItems.Any())
                    {
                        _logger.LogInformation("Found {ItemCount} types of items to release for OrderId {OrderId} from SagaData.", reservedItems.Count, orderId);
                        foreach (var item in reservedItems)
                        {
                            // Simulate adding stock back
                            _inventory[item.ProductId] = _inventory.GetValueOrDefault(item.ProductId, 0) + item.Quantity;
                            itemsToReleaseDetails.Add(new ReleasedItemDto(item.ProductId, item.Quantity));
                            _logger.LogInformation("Released {Quantity} of ProductId {ProductId} for OrderId {OrderId}.", item.Quantity, item.ProductId, orderId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No items found or deserialized from SagaData for OrderId {OrderId} to release.", orderId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize reserved items from SagaData for OrderId {OrderId}.", orderId);
                    // Potentially publish a StockReleasedEvent with empty items or a specific error note if critical
                }
            }
            else
            {
                _logger.LogWarning("No 'ReservedItems_{OrderId}' found in SagaData for OrderId {OrderId}, or data is not a string. Cannot determine items to release.", orderId);
                // If this is critical, a specific failure event might be needed.
                // For now, we'll publish StockReleasedEvent with potentially empty itemsToReleaseDetails.
            }

            // Publish StockReleasedEvent
            var stockReleasedEvent = new StockReleasedEvent(sagaId, orderId, itemsToReleaseDetails);
            try
            {
                _logger.LogInformation("Publishing StockReleasedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    stockReleasedEvent.MessageId, orderId, sagaId);
                await _messagePublisher.PublishAsync("saga_events_exchange", "order.stock.released", stockReleasedEvent);
                _logger.LogInformation("Successfully published StockReleasedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    stockReleasedEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(StockReleasedEvent), StepStatus.Completed, new { EventMessageId = stockReleasedEvent.MessageId, stockReleasedEvent.OrderId, stockReleasedEvent.SagaId, ItemsCount = stockReleasedEvent.ItemsReleased.Count });

                // Reload sagaData as it might have been modified or to ensure consistency
                sagaData = await _sagaStore.LoadSagaDataAsync(sagaId); 
                if (sagaData != null)
                {
                    sagaData.Extras["InventoryServiceStatus"] = "Compensated_StockReleased";
                    sagaData.Extras["OverallStatus"] = "Compensating_Chain_StockReleased"; // Or simply "Compensated"
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    sagaData.Extras["ReasonForStockRelease"] = reasonForRelease;
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                }
                else
                {
                     _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} during stock release compensation. Creating fallback.", sagaId);
                     var fallbackSagaData = new InventorySagaData { Extras = {
                        ["OrderId"] = orderId.ToString(),
                        ["SagaType"] = "OrderPlacementSaga",
                        ["OverallStatus"] = "Compensating_Chain_StockReleased_Recovered",
                        ["InventoryServiceStatus"] = "Compensated_StockReleased",
                        ["ReasonForStockRelease"] = reasonForRelease,
                        ["Error"] = "SagaData was missing, recovered in StockReservationService during release.",
                        ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
                    }};
                    if (!fallbackSagaData.Extras.ContainsKey("CreatedAt")) fallbackSagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, fallbackSagaData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish StockReleasedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    stockReleasedEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(StockReleasedEvent), StepStatus.Failed, new { Error = ex.Message, Note = "Publishing StockReleasedEvent failed", EventMessageId = stockReleasedEvent.MessageId });
                
                sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null) {
                    sagaData.Extras["InventoryServiceStatus"] = "Failed_EventPublishError_StockReleased";
                    sagaData.Extras["ErrorDetails_StockReservationService"] = $"Publishing StockReleasedEvent failed: {ex.Message}";
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                }
                // Depending on policy, this might re-throw or the saga might need another compensation trigger.
            }
        }
    }
}
