using System;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions;
using Lycia.Saga; // For StepStatus
using Lycia.Saga.Abstractions; // For ISagaStore
using Sample.OrderService.API.Dtos.IncomingInventory; // For StockReleasedEventDto
using Sample.OrderService.API.Models; // For OrderSagaData
using Sample.OrderService.API.Services;
using Microsoft.Extensions.Logging;

namespace Sample.OrderService.API.EventHandlers
{
    public class StockReleasedEventHandler : IEventHandler<StockReleasedEventDto>
    {
        private readonly OrderCreationService _orderService; // Assuming CancelOrderAsync is in OrderCreationService
        private readonly ISagaStore _sagaStore;
        private readonly ILogger<StockReleasedEventHandler> _logger;

        public StockReleasedEventHandler(
            OrderCreationService orderService,
            ISagaStore sagaStore,
            ILogger<StockReleasedEventHandler> logger)
        {
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(StockReleasedEventDto anEvent)
        {
            _logger.LogInformation(
                "Handling StockReleasedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}. ItemsReleasedCount: {ItemsReleasedCount}", 
                anEvent.MessageId, anEvent.OrderId, anEvent.SagaId, anEvent.ItemsReleased?.Count ?? 0);

            await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(StockReleasedEventDto), StepStatus.Processing, 
                new { anEvent.OrderId, ItemsReleasedCount = anEvent.ItemsReleased?.Count ?? 0, EventMessageId = anEvent.MessageId });

            var sagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId);
            if (sagaData != null)
            {
                sagaData.Extras["OverallStatus"] = "Compensating_Order";
                sagaData.Extras["OrderServiceStatus"] = "Processing_StockReleasedEvent";
                sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                sagaData.Extras["StockReleasedDetails"] = $"Items released: {anEvent.ItemsReleased?.Count ?? 0}";
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogInformation("SagaData updated to 'Compensating_Order' for SagaId: {SagaId}", anEvent.SagaId);
            }
            else
            {
                _logger.LogWarning("SagaData is NULL for SagaId: {SagaId} when handling StockReleasedEvent. Creating fallback.", anEvent.SagaId);
                sagaData = new OrderSagaData
                {
                    Extras =
                    {
                        ["OrderId"] = anEvent.OrderId.ToString(),
                        ["SagaType"] = "OrderPlacementSaga", 
                        ["OverallStatus"] = "Compensating_Order_Recovered",
                        ["OrderServiceStatus"] = "Processing_StockReleasedEvent",
                        ["StockReleasedDetails"] = $"Items released: {anEvent.ItemsReleased?.Count ?? 0}",
                        ["Error"] = "SagaData was missing, recovered in StockReleasedEventHandler.",
                        ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
                    }
                };
                if (!sagaData.Extras.ContainsKey("CreatedAt")) sagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
            }

            try
            {
                string reason = $"Stock released for order {anEvent.OrderId}.";
                _logger.LogInformation("Calling OrderCreationService.CancelOrderAsync for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {ReasonForCancellation}", 
                    anEvent.OrderId, anEvent.SagaId, reason);
                await _orderService.CancelOrderAsync(anEvent.SagaId, anEvent.OrderId, reason);
                
                _logger.LogInformation("Order cancellation process initiated successfully for OrderId: {OrderId}, SagaId: {SagaId} due to stock release.", 
                    anEvent.OrderId, anEvent.SagaId);
                
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(StockReleasedEventDto), StepStatus.Completed, 
                    new { Note = "Delegated to CancelOrderAsync", EventMessageId = anEvent.MessageId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during order cancellation for OrderId: {OrderId}, SagaId: {SagaId} triggered by StockReleasedEvent (MessageId: {MessageId}).", 
                    anEvent.OrderId, anEvent.SagaId, anEvent.MessageId);
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(StockReleasedEventDto), StepStatus.Failed, 
                    new { Error = ex.Message, Note = "Exception in StockReleasedEventHandler when calling CancelOrderAsync", EventMessageId = anEvent.MessageId });
                
                var currentSagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId) ?? sagaData;
                currentSagaData.Extras["OverallStatus"] = "Failed_Compensation_Order"; 
                currentSagaData.Extras["OrderServiceStatus"] = "Failed_StockReleasedEventHandlerError";
                currentSagaData.Extras["ErrorDetails_StockReleasedEventHandler"] = $"SagaId: {anEvent.SagaId}, OrderId: {anEvent.OrderId}, Exception: {ex.Message}";
                currentSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, currentSagaData);
                throw; 
            }
        }
    }
}
