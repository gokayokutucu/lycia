using System;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions;
using Lycia.Saga; // For StepStatus
using Lycia.Saga.Abstractions; // For ISagaStore
using Sample.OrderService.API.Dtos.IncomingInventory; // For StockReservationFailedEventDto
using Sample.OrderService.API.Models; // For OrderSagaData
using Sample.OrderService.API.Services;
using Microsoft.Extensions.Logging;

namespace Sample.OrderService.API.EventHandlers
{
    public class StockReservationFailedEventHandler : IEventHandler<StockReservationFailedEventDto>
    {
        private readonly OrderCreationService _orderService;
        private readonly ISagaStore _sagaStore;
        private readonly ILogger<StockReservationFailedEventHandler> _logger;

        public StockReservationFailedEventHandler(
            OrderCreationService orderService,
            ISagaStore sagaStore,
            ILogger<StockReservationFailedEventHandler> logger)
        {
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(StockReservationFailedEventDto anEvent)
        {
            _logger.LogInformation(
                "Handling StockReservationFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {Reason}", 
                anEvent.MessageId, anEvent.OrderId, anEvent.SagaId, anEvent.Reason);

            await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(StockReservationFailedEventDto), StepStatus.Processing, 
                new { anEvent.OrderId, anEvent.Reason, EventMessageId = anEvent.MessageId });

            var sagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId);
            if (sagaData != null)
            {
                sagaData.Extras["OverallStatus"] = "Compensating_Order";
                sagaData.Extras["OrderServiceStatus"] = "Processing_StockReservationFailedEvent";
                sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                sagaData.Extras["StockReservationFailureReasonReceived"] = anEvent.Reason;
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogInformation("SagaData updated to 'Compensating_Order' for SagaId: {SagaId} due to StockReservationFailedEvent.", anEvent.SagaId);
            }
            else
            {
                _logger.LogWarning("SagaData is NULL for SagaId: {SagaId} when handling StockReservationFailedEvent. Creating fallback.", anEvent.SagaId);
                sagaData = new OrderSagaData
                {
                    Extras =
                    {
                        ["OrderId"] = anEvent.OrderId.ToString(),
                        ["SagaType"] = "OrderPlacementSaga", 
                        ["OverallStatus"] = "Compensating_Order_Recovered",
                        ["OrderServiceStatus"] = "Processing_StockReservationFailedEvent",
                        ["StockReservationFailureReasonReceived"] = anEvent.Reason,
                        ["Error"] = "SagaData was missing, recovered in StockReservationFailedEventHandler.",
                        ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
                    }
                };
                if (!sagaData.Extras.ContainsKey("CreatedAt")) sagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
            }

            try
            {
                string reasonForCancellation = $"Stock reservation failed for order {anEvent.OrderId}. Reason: {anEvent.Reason}";
                _logger.LogInformation("Calling OrderCreationService.CancelOrderAsync for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {ReasonForCancellation}", 
                    anEvent.OrderId, anEvent.SagaId, reasonForCancellation);
                await _orderService.CancelOrderAsync(anEvent.SagaId, anEvent.OrderId, reasonForCancellation);
                
                _logger.LogInformation("Order cancellation process initiated successfully for OrderId: {OrderId}, SagaId: {SagaId} due to stock reservation failure.", 
                    anEvent.OrderId, anEvent.SagaId);
                
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(StockReservationFailedEventDto), StepStatus.Completed, 
                    new { Note = "Delegated to CancelOrderAsync", EventMessageId = anEvent.MessageId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during order cancellation for OrderId: {OrderId}, SagaId: {SagaId} triggered by StockReservationFailedEvent (MessageId: {MessageId}).", 
                    anEvent.OrderId, anEvent.SagaId, anEvent.MessageId);
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(StockReservationFailedEventDto), StepStatus.Failed, 
                    new { Error = ex.Message, Note = "Exception in StockReservationFailedEventHandler when calling CancelOrderAsync", EventMessageId = anEvent.MessageId });
                
                var currentSagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId) ?? sagaData;
                currentSagaData.Extras["OverallStatus"] = "Failed_Compensation_Order"; 
                currentSagaData.Extras["OrderServiceStatus"] = "Failed_StockReservationFailedEventHandlerError";
                currentSagaData.Extras["ErrorDetails_StockReservationFailedEventHandler"] = $"SagaId: {anEvent.SagaId}, OrderId: {anEvent.OrderId}, Exception: {ex.Message}";
                currentSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, currentSagaData);
                throw; 
            }
        }
    }
}
