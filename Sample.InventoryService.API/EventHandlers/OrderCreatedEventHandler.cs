using System;
using System.Linq;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions;
using Lycia.Saga; // For StepStatus
using Lycia.Saga.Abstractions; // For ISagaStore
using Sample.InventoryService.API.Dtos.IncomingOrder; // For OrderCreatedEventDto
using Sample.InventoryService.API.Models; // For InventorySagaData
using Sample.InventoryService.API.Services;
using Microsoft.Extensions.Logging;

namespace Sample.InventoryService.API.EventHandlers
{
    public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEventDto>
    {
        private readonly StockReservationService _stockReservationService;
        private readonly ISagaStore _sagaStore;
        private readonly ILogger<OrderCreatedEventHandler> _logger;

        public OrderCreatedEventHandler(
            StockReservationService stockReservationService,
            ISagaStore sagaStore, // Injected ISagaStore
            ILogger<OrderCreatedEventHandler> logger)
        {
            _stockReservationService = stockReservationService ?? throw new ArgumentNullException(nameof(stockReservationService));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore)); // Store injected ISagaStore
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(OrderCreatedEventDto anEvent)
        {
            _logger.LogInformation(
                "Handling OrderCreatedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                anEvent.MessageId, anEvent.OrderId, anEvent.SagaId);

            if (anEvent.OrderDetails?.Items == null)
            {
                _logger.LogWarning(
                    "OrderCreatedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId} has null or empty OrderDetails.Items. Cannot reserve stock.", 
                    anEvent.MessageId, anEvent.OrderId, anEvent.SagaId);
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(OrderCreatedEventDto), StepStatus.Failed, new { Error = "Missing order items in event.", EventMessageId = anEvent.MessageId });
                return;
            }

            // Log "Processing" step for this handler
            await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(OrderCreatedEventDto), StepStatus.Processing, new { anEvent.OrderId, EventMessageId = anEvent.MessageId });
            
            var sagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId);
            if (sagaData != null)
            {
                sagaData.Extras["OverallStatus"] = "InventoryProcessing";
                sagaData.Extras["InventoryServiceStatus"] = "Processing_OrderCreatedEvent"; // Indicate this handler is processing
                sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogInformation("SagaData updated to 'InventoryProcessing' for SagaId: {SagaId}", anEvent.SagaId);
            }
            else
            {
                _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} when handling OrderCreatedEvent. This is unexpected as OrderService should have created it.", anEvent.SagaId);
                // For resilience, create a default saga data if it's missing, though this indicates an issue.
                sagaData = new InventorySagaData(); // Use the concrete type
                sagaData.Extras["OrderId"] = anEvent.OrderId.ToString();
                sagaData.Extras["SagaType"] = "OrderPlacementSaga"; // Assuming same saga type
                sagaData.Extras["OverallStatus"] = "InventoryProcessing_Recovered"; // Special status
                sagaData.Extras["InventoryServiceStatus"] = "Processing_OrderCreatedEvent";
                sagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o"); // New creation time for this part
                sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                sagaData.Extras["Error"] = "SagaData was missing, recovered in InventoryService.";
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogWarning("Created and saved new SagaData for SagaId: {SagaId} due to missing state.", anEvent.SagaId);
            }
            
            var itemsToReserve = anEvent.OrderDetails.Items.Select(item => new Dtos.OrderItemDto
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity
            }).ToList();

            try
            {
                _logger.LogDebug("Calling StockReservationService.ReserveStockAsync for OrderId: {OrderId}, SagaId: {SagaId}", anEvent.OrderId, anEvent.SagaId);
                // The StockReservationService will now handle its own step logging and SagaData updates related to its specific actions.
                bool reservationResult = await _stockReservationService.ReserveStockAsync(anEvent.SagaId, anEvent.OrderId, itemsToReserve);
                
                if (reservationResult)
                {
                    _logger.LogInformation("Stock reservation process initiated successfully by StockReservationService for OrderId: {OrderId}, SagaId: {SagaId}.", 
                        anEvent.OrderId, anEvent.SagaId);
                }
                else
                {
                    _logger.LogWarning("Stock reservation process failed as reported by StockReservationService for OrderId: {OrderId}, SagaId: {SagaId}.", 
                        anEvent.OrderId, anEvent.SagaId);
                }
                
                // Log "Completed" step for this handler's specific task
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(OrderCreatedEventDto), StepStatus.Completed, new { Note = "Processing delegated to StockReservationService", ReservationSuccess = reservationResult, EventMessageId = anEvent.MessageId });
                _logger.LogInformation("Logged 'Completed' step for OrderCreatedEventHandler (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    anEvent.MessageId, anEvent.OrderId, anEvent.SagaId);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stock reservation for OrderId: {OrderId}, SagaId: {SagaId} triggered by OrderCreatedEvent (MessageId: {MessageId}).", 
                    anEvent.OrderId, anEvent.SagaId, anEvent.MessageId);
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(OrderCreatedEventDto), StepStatus.Failed, new { Error = ex.Message, Note = "Exception in OrderCreatedEventHandler", EventMessageId = anEvent.MessageId });
                
                var currentSagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId);
                if (currentSagaData != null)
                {
                    currentSagaData.Extras["OverallStatus"] = "Failed_InventoryProcessing";
                    currentSagaData.Extras["InventoryServiceStatus"] = "Failed_OrderCreatedEvent";
                    currentSagaData.Extras["ErrorDetails_OrderCreatedEventHandler"] = $"SagaId: {anEvent.SagaId}, OrderId: {anEvent.OrderId}, Exception: {ex.Message}";
                    currentSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, currentSagaData);
                }
                throw; 
            }
        }
    }
}
