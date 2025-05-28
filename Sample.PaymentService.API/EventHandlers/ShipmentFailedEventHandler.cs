using System;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions;
using Lycia.Saga; // For StepStatus
using Lycia.Saga.Abstractions; // For ISagaStore
using Sample.PaymentService.API.Dtos.IncomingShipment; // For ShipmentFailedEventDto
using Sample.PaymentService.API.Models; // For PaymentSagaData
using Sample.PaymentService.API.Services;
using Microsoft.Extensions.Logging;

namespace Sample.PaymentService.API.EventHandlers
{
    public class ShipmentFailedEventHandler : IEventHandler<ShipmentFailedEventDto>
    {
        private readonly PaymentProcessingService _paymentProcessingService;
        private readonly ISagaStore _sagaStore;
        private readonly ILogger<ShipmentFailedEventHandler> _logger;

        public ShipmentFailedEventHandler(
            PaymentProcessingService paymentProcessingService,
            ISagaStore sagaStore,
            ILogger<ShipmentFailedEventHandler> logger)
        {
            _paymentProcessingService = paymentProcessingService ?? throw new ArgumentNullException(nameof(paymentProcessingService));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(ShipmentFailedEventDto anEvent)
        {
            _logger.LogInformation(
                "Handling ShipmentFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {Reason}", 
                anEvent.MessageId, anEvent.OrderId, anEvent.SagaId, anEvent.Reason);

            await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(ShipmentFailedEventDto), StepStatus.Processing, 
                new { anEvent.OrderId, anEvent.Reason, EventMessageId = anEvent.MessageId });

            var sagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId);
            if (sagaData != null)
            {
                sagaData.Extras["OverallStatus"] = "Compensating_Payment"; // Mark that payment compensation is starting
                sagaData.Extras["PaymentServiceStatus"] = "Processing_ShipmentFailureEvent";
                sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                sagaData.Extras["ShipmentFailureReasonReceived"] = anEvent.Reason; // Store the reason for context
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogInformation("SagaData updated to 'Compensating_Payment' for SagaId: {SagaId}", anEvent.SagaId);
            }
            else
            {
                _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} when handling ShipmentFailedEvent. This might indicate a preceding issue or unexpected event order.", anEvent.SagaId);
                // Create fallback SagaData as this is a compensating action, state should ideally exist.
                sagaData = new PaymentSagaData
                {
                    Extras =
                    {
                        ["OrderId"] = anEvent.OrderId.ToString(),
                        ["SagaType"] = "OrderPlacementSaga", // Assuming
                        ["OverallStatus"] = "Compensating_Payment_Recovered",
                        ["PaymentServiceStatus"] = "Processing_ShipmentFailureEvent",
                        ["ShipmentFailureReasonReceived"] = anEvent.Reason,
                        ["Error"] = "SagaData was missing, recovered in PaymentService ShipmentFailedEventHandler.",
                        ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
                    }
                };
                if (!sagaData.Extras.ContainsKey("CreatedAt")) sagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o"); // Or a timestamp from the event if available
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogWarning("Created and saved new SagaData for SagaId: {SagaId} due to missing state during compensation.", anEvent.SagaId);
            }

            try
            {
                _logger.LogInformation("Calling PaymentProcessingService.RefundPaymentAsync for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {ReasonForRefund}", 
                    anEvent.OrderId, anEvent.SagaId, anEvent.Reason);
                await _paymentProcessingService.RefundPaymentAsync(anEvent.SagaId, anEvent.OrderId, anEvent.Reason);
                
                _logger.LogInformation("Payment refund process initiated successfully by PaymentProcessingService for OrderId: {OrderId}, SagaId: {SagaId}.", 
                    anEvent.OrderId, anEvent.SagaId);
                
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(ShipmentFailedEventDto), StepStatus.Completed, 
                    new { Note = "Delegated to RefundPaymentAsync", EventMessageId = anEvent.MessageId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during refund delegation for OrderId: {OrderId}, SagaId: {SagaId} triggered by ShipmentFailedEvent (MessageId: {MessageId}).", 
                    anEvent.OrderId, anEvent.SagaId, anEvent.MessageId);
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(ShipmentFailedEventDto), StepStatus.Failed, 
                    new { Error = ex.Message, Note = "Exception in ShipmentFailedEventHandler when calling RefundPaymentAsync", EventMessageId = anEvent.MessageId });
                
                // Reload sagaData as it might have been changed by RefundPaymentAsync before an exception
                var currentSagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId) ?? sagaData; // Use recently created if it was null
                currentSagaData.Extras["OverallStatus"] = "Failed_Compensation_Payment"; // More specific overall status
                currentSagaData.Extras["PaymentServiceStatus"] = "Failed_ShipmentFailedEventHandlerError";
                currentSagaData.Extras["ErrorDetails_ShipmentFailedEventHandler"] = $"SagaId: {anEvent.SagaId}, OrderId: {anEvent.OrderId}, Exception: {ex.Message}";
                currentSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, currentSagaData);
                throw; // Re-throw to ensure message is NACKed if not already handled by RefundPaymentAsync's exception logic
            }
        }
    }
}
