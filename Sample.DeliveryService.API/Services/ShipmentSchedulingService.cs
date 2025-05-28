using System;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions; // For IMessagePublisher
using Lycia.Saga; // For StepStatus, SagaData
using Lycia.Saga.Abstractions; // For ISagaStore
using Sample.DeliveryService.API.Events;
using Sample.DeliveryService.API.Models; // For DeliverySagaData
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sample.DeliveryService.API.Services
{
    public class ShipmentSchedulingService
    {
        private readonly IMessagePublisher _messagePublisher;
        private readonly ISagaStore _sagaStore;
        private readonly ILogger<ShipmentSchedulingService> _logger;

        private const string UnreachableAddressToken = "UNREACHABLE_ADDRESS";
        private const string InvalidAddressToken = "INVALID_ADDRESS_FORMAT";

        public ShipmentSchedulingService(
            IMessagePublisher messagePublisher, 
            ISagaStore sagaStore, // Injected ISagaStore
            ILogger<ShipmentSchedulingService>? logger)
        {
            _messagePublisher = messagePublisher ?? throw new ArgumentNullException(nameof(messagePublisher));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore)); // Store injected ISagaStore
            _logger = logger ?? NullLogger<ShipmentSchedulingService>.Instance;
        }

        public async Task<bool> ScheduleShipmentAsync(Guid sagaId, Guid orderId, object shippingAddressDetails)
        {
            _logger.LogInformation("Attempting shipment scheduling for OrderId: {OrderId}, SagaId: {SagaId}", orderId, sagaId);
            await _sagaStore.LogStepAsync(sagaId, typeof(ShipmentSchedulingService), StepStatus.Processing, new { OrderId = orderId, AddressDetails = shippingAddressDetails?.ToString() });

            string? addressToken = shippingAddressDetails?.ToString();
            string failureReason;

            if (string.IsNullOrWhiteSpace(addressToken))
            {
                failureReason = "Invalid or missing shipping address details.";
                _logger.LogWarning("Shipment scheduling failed for OrderId: {OrderId}. Reason: {Reason}", orderId, failureReason);
                await PublishShipmentFailedEventAndUpdateSaga(sagaId, orderId, failureReason);
                return false;
            }
            
            if (addressToken.Equals(UnreachableAddressToken, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "Delivery address is unreachable.";
                _logger.LogWarning("Shipment scheduling failed for OrderId: {OrderId}. Reason: {Reason}", orderId, failureReason);
                await PublishShipmentFailedEventAndUpdateSaga(sagaId, orderId, failureReason);
                return false;
            }
            else if (addressToken.Equals(InvalidAddressToken, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "Invalid address format provided.";
                _logger.LogWarning("Shipment scheduling failed for OrderId: {OrderId}. Reason: {Reason}", orderId, failureReason);
                await PublishShipmentFailedEventAndUpdateSaga(sagaId, orderId, failureReason);
                return false;
            }
            else 
            {
                string shipmentTrackingId = $"TRACK-{Guid.NewGuid().ToString().ToUpperInvariant().Substring(0, 12)}";
                _logger.LogInformation("Shipment scheduled successfully for OrderId: {OrderId}. TrackingId: {ShipmentTrackingId}", orderId, shipmentTrackingId);
                await PublishShipmentScheduledEventAndUpdateSaga(sagaId, orderId, shipmentTrackingId);
                return true;
            }
        }

        private async Task PublishShipmentScheduledEventAndUpdateSaga(Guid sagaId, Guid orderId, string shipmentTrackingId)
        {
            var shipmentScheduledEvent = new ShipmentScheduledEvent(sagaId, orderId, shipmentTrackingId);
            try
            {
                _logger.LogInformation("Publishing ShipmentScheduledEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    shipmentScheduledEvent.MessageId, orderId, sagaId);
                await _messagePublisher.PublishAsync("saga_events_exchange", "order.shipment.scheduled", shipmentScheduledEvent);
                _logger.LogInformation("Successfully published ShipmentScheduledEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    shipmentScheduledEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(ShipmentScheduledEvent), StepStatus.Completed, new { EventMessageId = shipmentScheduledEvent.MessageId, shipmentScheduledEvent.OrderId, shipmentScheduledEvent.SagaId, shipmentScheduledEvent.ShipmentTrackingId });

                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null)
                {
                    sagaData.Extras["OverallStatus"] = "Completed"; // Saga successful
                    sagaData.Extras["DeliveryServiceStatus"] = "Completed_ShipmentScheduled";
                    sagaData.Extras["ShipmentTrackingId"] = shipmentTrackingId;
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                } else { await CreateFallbackSagaData(sagaId, orderId, "Completed_Recovered", "Completed_ShipmentScheduled", shipmentTrackingId: shipmentTrackingId); }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish ShipmentScheduledEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    shipmentScheduledEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(ShipmentScheduledEvent), StepStatus.Failed, new { Error = ex.Message, Note = "Publishing ShipmentScheduledEvent failed", EventMessageId = shipmentScheduledEvent.MessageId });
                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null) {
                    sagaData.Extras["DeliveryServiceStatus"] = "Failed_EventPublishError_ShipmentScheduled";
                    sagaData.Extras["ErrorDetails_DeliveryService"] = $"Publishing ShipmentScheduledEvent failed: {ex.Message}";
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                }
                throw; 
            }
        }

        private async Task PublishShipmentFailedEventAndUpdateSaga(Guid sagaId, Guid orderId, string reason)
        {
            var shipmentFailedEvent = new ShipmentFailedEvent(sagaId, orderId, reason);
            try
            {
                _logger.LogInformation("Publishing ShipmentFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {Reason}", 
                    shipmentFailedEvent.MessageId, orderId, sagaId, reason);
                await _messagePublisher.PublishAsync("saga_events_exchange", "order.shipment.failed", shipmentFailedEvent);
                _logger.LogInformation("Successfully published ShipmentFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {Reason}", 
                    shipmentFailedEvent.MessageId, orderId, sagaId, reason);
                await _sagaStore.LogStepAsync(sagaId, typeof(ShipmentFailedEvent), StepStatus.Completed, new { EventMessageId = shipmentFailedEvent.MessageId, shipmentFailedEvent.OrderId, shipmentFailedEvent.SagaId, shipmentFailedEvent.Reason });

                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null)
                {
                    sagaData.Extras["OverallStatus"] = "Failed_ShipmentScheduling";
                    sagaData.Extras["DeliveryServiceStatus"] = "Failed_Shipment";
                    sagaData.Extras["ReasonForShipmentFailure"] = reason;
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                } else { await CreateFallbackSagaData(sagaId, orderId, "Failed_ShipmentScheduling_Recovered", "Failed_Shipment", reasonForFailure: reason); }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish ShipmentFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    shipmentFailedEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(ShipmentFailedEvent), StepStatus.Failed, new { Error = ex.Message, Note = "Publishing ShipmentFailedEvent failed", EventMessageId = shipmentFailedEvent.MessageId });
                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null) {
                    sagaData.Extras["DeliveryServiceStatus"] = "Failed_EventPublishError_ShipmentFailed";
                    sagaData.Extras["ErrorDetails_DeliveryService"] = $"Publishing ShipmentFailedEvent failed: {ex.Message}";
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                }
                throw; 
            }
        }
        
        private async Task CreateFallbackSagaData(Guid sagaId, Guid orderId, string overallStatus, string deliveryStatus, string? shipmentTrackingId = null, string? reasonForFailure = null)
        {
            _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} during shipment scheduling. Creating fallback.", sagaId);
            var sagaData = new DeliverySagaData { Extras = {
                ["OrderId"] = orderId.ToString(),
                ["SagaType"] = "OrderPlacementSaga",
                ["OverallStatus"] = overallStatus,
                ["DeliveryServiceStatus"] = deliveryStatus,
                ["Error"] = "SagaData was missing, recovered in DeliveryService.",
                ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
            }};
            if (!sagaData.Extras.ContainsKey("CreatedAt")) sagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o");
            if (!string.IsNullOrEmpty(shipmentTrackingId)) sagaData.Extras["ShipmentTrackingId"] = shipmentTrackingId;
            if (!string.IsNullOrEmpty(reasonForFailure)) sagaData.Extras["ReasonForShipmentFailure"] = reasonForFailure;
            await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
        }
    }
}
