using System;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions;
using Lycia.Saga; // For StepStatus
using Lycia.Saga.Abstractions; // For ISagaStore
using Sample.InventoryService.API.Dtos.IncomingPayment; // For PaymentRefundedEventDto
using Sample.InventoryService.API.Models; // For InventorySagaData
using Sample.InventoryService.API.Services;
using Microsoft.Extensions.Logging;

namespace Sample.InventoryService.API.EventHandlers
{
    public class PaymentRefundedEventHandler : IEventHandler<PaymentRefundedEventDto>
    {
        private readonly StockReservationService _stockReservationService;
        private readonly ISagaStore _sagaStore;
        private readonly ILogger<PaymentRefundedEventHandler> _logger;

        public PaymentRefundedEventHandler(
            StockReservationService stockReservationService,
            ISagaStore sagaStore,
            ILogger<PaymentRefundedEventHandler> logger)
        {
            _stockReservationService = stockReservationService ?? throw new ArgumentNullException(nameof(stockReservationService));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(PaymentRefundedEventDto anEvent)
        {
            _logger.LogInformation(
                "Handling PaymentRefundedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}, RefundTransactionId: {RefundTransactionId}", 
                anEvent.MessageId, anEvent.OrderId, anEvent.SagaId, anEvent.RefundTransactionId);

            await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(PaymentRefundedEventDto), StepStatus.Processing, 
                new { anEvent.OrderId, anEvent.RefundTransactionId, EventMessageId = anEvent.MessageId });

            var sagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId);
            if (sagaData != null)
            {
                sagaData.Extras["OverallStatus"] = "Compensating_Inventory"; // Mark that inventory compensation is starting
                sagaData.Extras["InventoryServiceStatus"] = "Processing_PaymentRefundEvent";
                sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                sagaData.Extras["PaymentRefundTransactionIdReceived"] = anEvent.RefundTransactionId; // Store for context
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogInformation("SagaData updated to 'Compensating_Inventory' for SagaId: {SagaId}", anEvent.SagaId);
            }
            else
            {
                _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} when handling PaymentRefundedEvent. This might indicate a preceding issue.", anEvent.SagaId);
                // Create fallback SagaData as this is a compensating action, state should ideally exist.
                sagaData = new InventorySagaData
                {
                    Extras =
                    {
                        ["OrderId"] = anEvent.OrderId.ToString(),
                        ["SagaType"] = "OrderPlacementSaga", // Assuming
                        ["OverallStatus"] = "Compensating_Inventory_Recovered",
                        ["InventoryServiceStatus"] = "Processing_PaymentRefundEvent",
                        ["PaymentRefundTransactionIdReceived"] = anEvent.RefundTransactionId,
                        ["Error"] = "SagaData was missing, recovered in InventoryService PaymentRefundedEventHandler.",
                        ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
                    }
                };
                if (!sagaData.Extras.ContainsKey("CreatedAt")) sagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogWarning("Created and saved new SagaData for SagaId: {SagaId} due to missing state during compensation.", anEvent.SagaId);
            }

            try
            {
                string reasonForRelease = $"Payment refunded with TxId: {anEvent.RefundTransactionId}";
                _logger.LogInformation("Calling StockReservationService.ReleaseStockAsync for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {ReasonForRelease}", 
                    anEvent.OrderId, anEvent.SagaId, reasonForRelease);
                
                // StockReservationService.ReleaseStockAsync will handle its own specific step logging and SagaData updates
                await _stockReservationService.ReleaseStockAsync(anEvent.SagaId, anEvent.OrderId, reasonForRelease);
                
                _logger.LogInformation("Stock release process initiated successfully by StockReservationService for OrderId: {OrderId}, SagaId: {SagaId}.", 
                    anEvent.OrderId, anEvent.SagaId);
                
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(PaymentRefundedEventDto), StepStatus.Completed, 
                    new { Note = "Delegated to ReleaseStockAsync", EventMessageId = anEvent.MessageId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stock release delegation for OrderId: {OrderId}, SagaId: {SagaId} triggered by PaymentRefundedEvent (MessageId: {MessageId}).", 
                    anEvent.OrderId, anEvent.SagaId, anEvent.MessageId);
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(PaymentRefundedEventDto), StepStatus.Failed, 
                    new { Error = ex.Message, Note = "Exception in PaymentRefundedEventHandler when calling ReleaseStockAsync", EventMessageId = anEvent.MessageId });
                
                // Reload sagaData as it might have been changed by ReleaseStockAsync before an exception
                var currentSagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId) ?? sagaData; 
                currentSagaData.Extras["OverallStatus"] = "Failed_Compensation_Inventory"; 
                currentSagaData.Extras["InventoryServiceStatus"] = "Failed_PaymentRefundedEventHandlerError";
                currentSagaData.Extras["ErrorDetails_PaymentRefundedEventHandler"] = $"SagaId: {anEvent.SagaId}, OrderId: {anEvent.OrderId}, Exception: {ex.Message}";
                currentSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, currentSagaData);
                throw; // Re-throw to ensure message is NACKed if not already handled by ReleaseStockAsync's exception logic
            }
        }
    }
}
