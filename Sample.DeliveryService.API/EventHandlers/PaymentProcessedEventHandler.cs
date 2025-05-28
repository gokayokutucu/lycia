using System;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions;
using Lycia.Saga; // For StepStatus
using Lycia.Saga.Abstractions; // For ISagaStore
using Sample.DeliveryService.API.Dtos.IncomingPayment; // For PaymentProcessedEventDto
using Sample.DeliveryService.API.Models; // For DeliverySagaData
using Sample.DeliveryService.API.Services;
using Microsoft.Extensions.Logging;

namespace Sample.DeliveryService.API.EventHandlers
{
    public class PaymentProcessedEventHandler : IEventHandler<PaymentProcessedEventDto>
    {
        private readonly ShipmentSchedulingService _shipmentSchedulingService;
        private readonly ISagaStore _sagaStore;
        private readonly ILogger<PaymentProcessedEventHandler> _logger;

        public PaymentProcessedEventHandler(
            ShipmentSchedulingService shipmentSchedulingService,
            ISagaStore sagaStore, // Injected ISagaStore
            ILogger<PaymentProcessedEventHandler> logger)
        {
            _shipmentSchedulingService = shipmentSchedulingService ?? throw new ArgumentNullException(nameof(shipmentSchedulingService));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore)); // Store injected ISagaStore
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(PaymentProcessedEventDto anEvent)
        {
            _logger.LogInformation(
                "Handling PaymentProcessedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}, PaymentConfirmationId: {PaymentConfirmationId}", 
                anEvent.MessageId, anEvent.OrderId, anEvent.SagaId, anEvent.PaymentConfirmationId);

            // Log "Processing" step for this handler
            await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(PaymentProcessedEventDto), StepStatus.Processing, 
                new { anEvent.OrderId, anEvent.PaymentConfirmationId, EventMessageId = anEvent.MessageId });

            var sagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId);
            if (sagaData != null)
            {
                sagaData.Extras["OverallStatus"] = "ShipmentProcessing";
                sagaData.Extras["DeliveryServiceStatus"] = "Processing_PaymentProcessedEvent";
                sagaData.Extras["PaymentConfirmationIdReceived"] = anEvent.PaymentConfirmationId; // Store confirmation ID
                sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogInformation("SagaData updated to 'ShipmentProcessing' for SagaId: {SagaId}", anEvent.SagaId);
            }
            else
            {
                _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} when handling PaymentProcessedEvent. This is unexpected.", anEvent.SagaId);
                sagaData = new DeliverySagaData // Use the concrete type
                {
                    Extras = 
                    {
                        ["OrderId"] = anEvent.OrderId.ToString(),
                        ["SagaType"] = "OrderPlacementSaga", // Assuming same saga type
                        ["OverallStatus"] = "ShipmentProcessing_Recovered", // Special status
                        ["DeliveryServiceStatus"] = "Processing_PaymentProcessedEvent",
                        ["PaymentConfirmationIdReceived"] = anEvent.PaymentConfirmationId,
                        ["Error"] = "SagaData was missing, recovered in DeliveryService PaymentProcessedEventHandler.",
                        ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
                    }
                };
                 if (!sagaData.Extras.ContainsKey("CreatedAt")) sagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogWarning("Created and saved new SagaData for SagaId: {SagaId} due to missing state.", anEvent.SagaId);
            }

            var shippingAddressDetails = await GetShippingAddressForOrderAsync(anEvent.OrderId);

            if (shippingAddressDetails == null)
            {
                _logger.LogError(
                    "Failed to retrieve shipping address for OrderId: {OrderId}, SagaId: {SagaId}, from PaymentProcessedEvent (MessageId: {MessageId}). Cannot schedule shipment.", 
                    anEvent.OrderId, anEvent.SagaId, anEvent.MessageId);
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(PaymentProcessedEventDto), StepStatus.Failed, new { Error = "Failed to retrieve shipping address.", EventMessageId = anEvent.MessageId });
                
                var currentSagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId) ?? sagaData;
                currentSagaData.Extras["OverallStatus"] = "Failed_ShipmentProcessing";
                currentSagaData.Extras["DeliveryServiceStatus"] = "Failed_ShippingAddressRetrieval";
                currentSagaData.Extras["ErrorDetails_PaymentProcessedEventHandler"] = $"SagaId: {anEvent.SagaId}, OrderId: {anEvent.OrderId}, Failed to retrieve shipping address.";
                currentSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, currentSagaData);
                return;
            }

            try
            {
                _logger.LogInformation(
                    "Calling ShipmentSchedulingService.ScheduleShipmentAsync for OrderId: {OrderId}, SagaId: {SagaId}", 
                    anEvent.OrderId, anEvent.SagaId);
                // ShipmentSchedulingService will now handle its own step logging and SagaData updates related to shipment events
                bool shipmentResult = await _shipmentSchedulingService.ScheduleShipmentAsync(anEvent.SagaId, anEvent.OrderId, shippingAddressDetails);
                
                _logger.LogInformation(
                    "ShipmentSchedulingService call completed for OrderId: {OrderId}, SagaId: {SagaId}. Result: {ShipmentResult}", 
                    anEvent.OrderId, anEvent.SagaId, shipmentResult);
                
                // Log "Completed" step for this handler's specific task of delegating to ShipmentSchedulingService
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(PaymentProcessedEventDto), StepStatus.Completed, new { Note = "Processing delegated to ShipmentSchedulingService", ShipmentInitiated = shipmentResult, EventMessageId = anEvent.MessageId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error during shipment scheduling delegation for OrderId: {OrderId}, SagaId: {SagaId} triggered by PaymentProcessedEvent (MessageId: {MessageId}).", 
                    anEvent.OrderId, anEvent.SagaId, anEvent.MessageId);
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(PaymentProcessedEventDto), StepStatus.Failed, new { Error = ex.Message, Note = "Exception in PaymentProcessedEventHandler when calling ShipmentSchedulingService", EventMessageId = anEvent.MessageId });
                
                var currentSagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId) ?? sagaData;
                currentSagaData.Extras["OverallStatus"] = "Failed_ShipmentProcessing";
                currentSagaData.Extras["DeliveryServiceStatus"] = "Failed_PaymentProcessedEventHandlerError";
                currentSagaData.Extras["ErrorDetails_PaymentProcessedEventHandler"] = $"SagaId: {anEvent.SagaId}, OrderId: {anEvent.OrderId}, Exception: {ex.Message}";
                currentSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, currentSagaData);
                throw; 
            }
        }
        
        private async Task<object> GetShippingAddressForOrderAsync(Guid orderId)
        {
            await Task.Delay(50); 
            if (orderId == Guid.Empty) return null;
            return new { Street = "123 Main St", City = "Anytown", PostalCode = "12345", Country = "SagaCountry" }.ToString();
        }
    }
}
