using System;
using System.Threading.Tasks;
using Sample.OrderService.API.Events;
using Lycia.Messaging.Abstractions; // For IMessagePublisher
using Lycia.Saga.Abstractions; // For ISagaStore, ISagaIdGenerator
using Lycia.Saga; // For StepStatus, SagaData
using Microsoft.Extensions.Logging; // For ILogger (optional, but good practice)
using Sample.OrderService.API.Models; // For OrderSagaData

namespace Sample.OrderService.API.Services
{
    public class OrderCreationService
    {
        private readonly IMessagePublisher _messagePublisher;
        private readonly ISagaStore _sagaStore;
        private readonly ISagaIdGenerator _sagaIdGenerator;
        private readonly ILogger<OrderCreationService> _logger;

        public OrderCreationService(
            IMessagePublisher messagePublisher,
            ISagaStore sagaStore,
            ISagaIdGenerator sagaIdGenerator,
            ILogger<OrderCreationService> logger) // Added ILogger
        {
            _messagePublisher = messagePublisher ?? throw new ArgumentNullException(nameof(messagePublisher));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore));
            _sagaIdGenerator = sagaIdGenerator ?? throw new ArgumentNullException(nameof(sagaIdGenerator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger)); // Added ILogger
        }

        public async Task<Guid> CreateOrderAsync(OrderDetailsDto orderDetails)
        {
            var orderId = Guid.NewGuid();
            var sagaId = _sagaIdGenerator.GenerateNewSagaId();

            _logger.LogInformation("Creating OrderId: {OrderId}, SagaId: {SagaId}", orderId, sagaId);

            // Create and save initial SagaData
            var sagaData = new OrderSagaData(); // Use the concrete type
            sagaData.Extras["OrderId"] = orderId.ToString();
            sagaData.Extras["SagaType"] = "OrderPlacementSaga";
            sagaData.Extras["OverallStatus"] = "PendingOrderCreation";
            sagaData.Extras["OrderServiceStatus"] = "Processing";
            sagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o");
            sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");

            await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
            _logger.LogInformation("Initial SagaData saved for SagaId: {SagaId}", sagaId);

            // Log the "Started" step for OrderCreatedEvent
            await _sagaStore.LogStepAsync(sagaId, typeof(Events.OrderCreatedEvent), StepStatus.Started, orderDetails);
            _logger.LogInformation("Logged 'Started' step for OrderCreatedEvent (OrderId: {OrderId}, SagaId: {SagaId})", orderId, sagaId);

            // Create the event payload
            var orderCreatedEvent = new OrderCreatedEvent(sagaId, orderId, orderDetails);

            // Publish the event
            try
            {
                _logger.LogInformation("Publishing OrderCreatedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    orderCreatedEvent.MessageId, orderId, sagaId);
                await _messagePublisher.PublishAsync("saga_events_exchange", "order.created", orderCreatedEvent);
                _logger.LogInformation("Successfully published OrderCreatedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    orderCreatedEvent.MessageId, orderId, sagaId);

                // Log the "Completed" step for OrderCreatedEvent
                await _sagaStore.LogStepAsync(sagaId, typeof(Events.OrderCreatedEvent), StepStatus.Completed, new { EventMessageId = orderCreatedEvent.MessageId, orderCreatedEvent.OrderId, orderCreatedEvent.SagaId });
                _logger.LogInformation("Logged 'Completed' step for OrderCreatedEvent (MessageId: {MessageId}) for SagaId: {SagaId}", orderCreatedEvent.MessageId, sagaId);

                // Load, update status, and save SagaData
                var currentSagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (currentSagaData != null)
                {
                    currentSagaData.Extras["OverallStatus"] = "OrderCreated_AwaitingStockReservation";
                    currentSagaData.Extras["OrderServiceStatus"] = "Completed";
                    currentSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, currentSagaData);
                    _logger.LogInformation("SagaData updated and saved for SagaId: {SagaId}", sagaId);
                }
                else
                {
                    _logger.LogWarning("SagaData was null after successful event publishing for SagaId: {SagaId}. Creating and saving new state.", sagaId);
                    var fallbackSagaData = new OrderSagaData(); // Use concrete type
                    fallbackSagaData.Extras["OrderId"] = orderId.ToString();
                    fallbackSagaData.Extras["SagaType"] = "OrderPlacementSaga";
                    fallbackSagaData.Extras["OverallStatus"] = "OrderCreated_AwaitingStockReservation"; // Reflect current state
                    fallbackSagaData.Extras["OrderServiceStatus"] = "Completed"; // Reflect current state
                    fallbackSagaData.Extras["CreatedAt"] = sagaData.Extras["CreatedAt"]; // Use original creation time
                    fallbackSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    // Potentially log other details known at this point
                    await _sagaStore.SaveSagaDataAsync(sagaId, fallbackSagaData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing OrderCreatedEvent (MessageId: {MessageId}) or updating Saga for OrderId: {OrderId}, SagaId: {SagaId}", 
                    orderCreatedEvent.MessageId, orderId, sagaId);
                // Log the "Failed" step for OrderCreatedEvent
                await _sagaStore.LogStepAsync(sagaId, typeof(Events.OrderCreatedEvent), StepStatus.Failed, new { Error = ex.Message, EventMessageId = orderCreatedEvent.MessageId });
                
                // Update SagaData to reflect failure if possible
                var failedSagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (failedSagaData != null)
                {
                    failedSagaData.Extras["OverallStatus"] = "Failed_OrderCreation";
                    failedSagaData.Extras["OrderServiceStatus"] = "Failed";
                    failedSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    failedSagaData.Extras["ErrorDetails"] = ex.Message;
                    await _sagaStore.SaveSagaDataAsync(sagaId, failedSagaData);
                }
                throw; 
            }

            return orderId;
        }

        public async Task CancelOrderAsync(Guid sagaId, Guid orderId, string reasonForCancellation)
        {
            _logger.LogInformation("Attempting to cancel OrderId: {OrderId} for SagaId: {SagaId}, Reason: {Reason}", orderId, sagaId, reasonForCancellation);
            await _sagaStore.LogStepAsync(sagaId, typeof(OrderCreationService), StepStatus.Compensating, new { OrderId = orderId, Action = "CancelOrder", Reason = reasonForCancellation });

            // Simulate order cancellation logic (e.g., update local DB status)
            _logger.LogInformation("Order cancellation simulated for OrderId: {OrderId}, SagaId: {SagaId}", orderId, sagaId);

            var orderCancelledEvent = new Events.OrderCancelledEvent(sagaId, orderId, reasonForCancellation);
            try
            {
                _logger.LogInformation("Publishing OrderCancelledEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    orderCancelledEvent.MessageId, orderId, sagaId);
                await _messagePublisher.PublishAsync("saga_events_exchange", "order.cancelled", orderCancelledEvent);
                _logger.LogInformation("Successfully published OrderCancelledEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    orderCancelledEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(Events.OrderCancelledEvent), StepStatus.Completed, new { EventMessageId = orderCancelledEvent.MessageId, orderCancelledEvent.OrderId, orderCancelledEvent.SagaId });

                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null)
                {
                    sagaData.Extras["OrderServiceStatus"] = "Compensated_OrderCancelled";
                    sagaData.Extras["OverallStatus"] = "Failed_RolledBack"; // Final failure state
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    sagaData.Extras["ReasonForCancellation"] = reasonForCancellation;
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                    _logger.LogInformation("SagaData updated to 'Failed_RolledBack' for SagaId: {SagaId}", sagaId);
                }
                else
                {
                    _logger.LogWarning("SagaData was null for SagaId: {SagaId} during order cancellation. Creating fallback.", sagaId);
                    var fallbackSagaData = new Models.OrderSagaData
                    {
                        Extras =
                        {
                            ["OrderId"] = orderId.ToString(),
                            ["SagaType"] = "OrderPlacementSaga",
                            ["OrderServiceStatus"] = "Compensated_OrderCancelled",
                            ["OverallStatus"] = "Failed_RolledBack_Recovered",
                            ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o"),
                            ["ReasonForCancellation"] = reasonForCancellation,
                            ["Error"] = "SagaData was missing, recovered in OrderCreationService.CancelOrderAsync"
                        }
                    };
                    if (!fallbackSagaData.Extras.ContainsKey("CreatedAt")) fallbackSagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, fallbackSagaData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish OrderCancelledEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    orderCancelledEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(Events.OrderCancelledEvent), StepStatus.Failed, new { Error = ex.Message, Note = "Publishing OrderCancelledEvent failed", EventMessageId = orderCancelledEvent.MessageId });
                
                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null)
                {
                    sagaData.Extras["OrderServiceStatus"] = "Failed_EventPublishError_OrderCancelled";
                    sagaData.Extras["ErrorDetails_OrderService"] = $"Publishing OrderCancelledEvent failed: {ex.Message}";
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                }
                // Depending on policy, this might re-throw or the saga might need another compensation trigger.
                // For now, the error is logged and saga state updated.
            }
        }
    }
}
