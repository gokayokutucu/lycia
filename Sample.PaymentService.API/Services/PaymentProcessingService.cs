using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions; // For IMessagePublisher
using Lycia.Saga; // For StepStatus, SagaData
using Lycia.Saga.Abstractions; // For ISagaStore
using Sample.PaymentService.API.Events;
using Sample.PaymentService.API.Models; // For PaymentSagaData
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sample.PaymentService.API.Services
{
    public class PaymentProcessingService
    {
        private readonly IMessagePublisher _messagePublisher;
        private readonly ISagaStore _sagaStore;
        private readonly ILogger<PaymentProcessingService> _logger;

        private static readonly List<string> _validPaymentTokens = new List<string> { "valid_token_123", "success_token_456" };
        private static readonly List<string> _insufficientFundsTokens = new List<string> { "funds_token_789" };

        public PaymentProcessingService(
            IMessagePublisher messagePublisher, 
            ISagaStore sagaStore, // Injected ISagaStore
            ILogger<PaymentProcessingService>? logger)
        {
            _messagePublisher = messagePublisher ?? throw new ArgumentNullException(nameof(messagePublisher));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore)); // Store injected ISagaStore
            _logger = logger ?? NullLogger<PaymentProcessingService>.Instance;
        }

        public async Task<bool> ProcessPaymentAsync(Guid sagaId, Guid orderId, decimal amount, object paymentDetailsToken)
        {
            _logger.LogInformation("Attempting payment processing for OrderId: {OrderId}, SagaId: {SagaId}, Amount: {Amount}", orderId, sagaId, amount);
            await _sagaStore.LogStepAsync(sagaId, typeof(PaymentProcessingService), StepStatus.Processing, new { OrderId = orderId, Amount = amount });

            string? tokenString = paymentDetailsToken?.ToString();
            string failureReason;

            if (string.IsNullOrWhiteSpace(tokenString))
            {
                failureReason = "Invalid or missing payment details token.";
                _logger.LogWarning("Payment processing failed for OrderId: {OrderId}. Reason: {Reason}", orderId, failureReason);
                await PublishPaymentFailedEventAndUpdateSaga(sagaId, orderId, failureReason);
                return false;
            }
            
            if (_validPaymentTokens.Contains(tokenString))
            {
                string paymentConfirmationId = $"CONF-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
                _logger.LogInformation("Payment processed successfully for OrderId: {OrderId}. ConfirmationId: {ConfirmationId}", orderId, paymentConfirmationId);
                await PublishPaymentProcessedEventAndUpdateSaga(sagaId, orderId, paymentConfirmationId);
                return true;
            }
            else if (_insufficientFundsTokens.Contains(tokenString))
            {
                failureReason = "Insufficient funds.";
                _logger.LogWarning("Payment processing failed for OrderId: {OrderId}. Reason: {Reason}", orderId, failureReason);
                await PublishPaymentFailedEventAndUpdateSaga(sagaId, orderId, failureReason);
                return false;
            }
            else
            {
                failureReason = "Payment gateway declined.";
                _logger.LogWarning("Payment processing failed for OrderId: {OrderId}. Reason: {Reason} (simulated).", orderId, failureReason);
                await PublishPaymentFailedEventAndUpdateSaga(sagaId, orderId, failureReason);
                return false;
            }
        }

        private async Task PublishPaymentProcessedEventAndUpdateSaga(Guid sagaId, Guid orderId, string paymentConfirmationId)
        {
            var paymentProcessedEvent = new PaymentProcessedEvent(sagaId, orderId, paymentConfirmationId);
            try
            {
                _logger.LogInformation("Publishing PaymentProcessedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    paymentProcessedEvent.MessageId, orderId, sagaId);
                await _messagePublisher.PublishAsync("saga_events_exchange", "order.payment.processed", paymentProcessedEvent);
                _logger.LogInformation("Successfully published PaymentProcessedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    paymentProcessedEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(PaymentProcessedEvent), StepStatus.Completed, new { EventMessageId = paymentProcessedEvent.MessageId, paymentProcessedEvent.OrderId, paymentProcessedEvent.SagaId, paymentProcessedEvent.PaymentConfirmationId });

                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null)
                {
                    sagaData.Extras["OverallStatus"] = "PaymentProcessed_AwaitingShipment";
                    sagaData.Extras["PaymentServiceStatus"] = "Completed_PaymentProcessed";
                    sagaData.Extras["PaymentConfirmationId"] = paymentConfirmationId;
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                } else { await CreateFallbackSagaData(sagaId, orderId, "PaymentProcessed_AwaitingShipment_Recovered", "Completed_PaymentProcessed", paymentConfirmationId: paymentConfirmationId); }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish PaymentProcessedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    paymentProcessedEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(PaymentProcessedEvent), StepStatus.Failed, new { Error = ex.Message, Note = "Publishing PaymentProcessedEvent failed", EventMessageId = paymentProcessedEvent.MessageId });
                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null) {
                    sagaData.Extras["PaymentServiceStatus"] = "Failed_EventPublishError_PaymentProcessed";
                    sagaData.Extras["ErrorDetails_PaymentService"] = $"Publishing PaymentProcessedEvent failed: {ex.Message}";
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                }
                throw; 
            }
        }

        private async Task PublishPaymentFailedEventAndUpdateSaga(Guid sagaId, Guid orderId, string reason)
        {
            var paymentFailedEvent = new PaymentFailedEvent(sagaId, orderId, reason);
            try
            {
                _logger.LogInformation("Publishing PaymentFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {Reason}", 
                    paymentFailedEvent.MessageId, orderId, sagaId, reason);
                await _messagePublisher.PublishAsync("saga_events_exchange", "order.payment.failed", paymentFailedEvent);
                _logger.LogInformation("Successfully published PaymentFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {Reason}", 
                    paymentFailedEvent.MessageId, orderId, sagaId, reason);
                await _sagaStore.LogStepAsync(sagaId, typeof(PaymentFailedEvent), StepStatus.Completed, new { EventMessageId = paymentFailedEvent.MessageId, paymentFailedEvent.OrderId, paymentFailedEvent.SagaId, paymentFailedEvent.Reason });

                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null)
                {
                    sagaData.Extras["OverallStatus"] = "Failed_PaymentProcessing";
                    sagaData.Extras["PaymentServiceStatus"] = "Failed_Payment";
                    sagaData.Extras["ReasonForPaymentFailure"] = reason;
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                } else { await CreateFallbackSagaData(sagaId, orderId, "Failed_PaymentProcessing_Recovered", "Failed_Payment", reasonForFailure: reason); }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish PaymentFailedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    paymentFailedEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(PaymentFailedEvent), StepStatus.Failed, new { Error = ex.Message, Note = "Publishing PaymentFailedEvent failed", EventMessageId = paymentFailedEvent.MessageId });
                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null) {
                    sagaData.Extras["PaymentServiceStatus"] = "Failed_EventPublishError_PaymentFailed";
                    sagaData.Extras["ErrorDetails_PaymentService"] = $"Publishing PaymentFailedEvent failed: {ex.Message}";
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                }
                throw; 
            }
        }

        private async Task CreateFallbackSagaData(Guid sagaId, Guid orderId, string overallStatus, string paymentStatus, string? paymentConfirmationId = null, string? reasonForFailure = null)
        {
            _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} during payment processing. Creating fallback.", sagaId);
            var sagaData = new PaymentSagaData { Extras = {
                ["OrderId"] = orderId.ToString(),
                ["SagaType"] = "OrderPlacementSaga",
                ["OverallStatus"] = overallStatus,
                ["PaymentServiceStatus"] = paymentStatus,
                ["Error"] = "SagaData was missing, recovered in PaymentService.",
                ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
            }};
            if (!sagaData.Extras.ContainsKey("CreatedAt")) sagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o");
            if (!string.IsNullOrEmpty(paymentConfirmationId)) sagaData.Extras["PaymentConfirmationId"] = paymentConfirmationId;
            if (!string.IsNullOrEmpty(reasonForFailure)) sagaData.Extras["ReasonForPaymentFailure"] = reasonForFailure;
            await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
        }

        public async Task RefundPaymentAsync(Guid sagaId, Guid orderId, string reasonForRefund)
        {
            _logger.LogInformation("Attempting to refund payment for OrderId: {OrderId}, SagaId: {SagaId}, Reason: {Reason}", orderId, sagaId, reasonForRefund);
            await _sagaStore.LogStepAsync(sagaId, typeof(PaymentProcessingService), StepStatus.Compensating, new { OrderId = orderId, Action = "RefundPayment", Reason = reasonForRefund });

            // Simulate refund logic
            string refundTransactionId = $"REFUND-{Guid.NewGuid().ToString().Substring(0, 10).ToUpper()}";
            _logger.LogInformation("Payment refund simulated for OrderId: {OrderId}. RefundTransactionId: {RefundTransactionId}", orderId, refundTransactionId);

            // Publish PaymentRefundedEvent
            var paymentRefundedEvent = new PaymentRefundedEvent(sagaId, orderId, refundTransactionId);
            try
            {
                _logger.LogInformation("Publishing PaymentRefundedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    paymentRefundedEvent.MessageId, orderId, sagaId);
                await _messagePublisher.PublishAsync("saga_events_exchange", "order.payment.refunded", paymentRefundedEvent);
                _logger.LogInformation("Successfully published PaymentRefundedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    paymentRefundedEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(PaymentRefundedEvent), StepStatus.Completed, new { EventMessageId = paymentRefundedEvent.MessageId, paymentRefundedEvent.OrderId, paymentRefundedEvent.SagaId, paymentRefundedEvent.RefundTransactionId });

                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null)
                {
                    sagaData.Extras["PaymentServiceStatus"] = "Compensated_PaymentRefunded";
                    sagaData.Extras["OverallStatus"] = "Compensating_Chain_PaymentRefunded"; // Or simply "Compensated" if this is the end of a specific compensation path
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    sagaData.Extras["RefundTransactionId"] = refundTransactionId;
                    sagaData.Extras["ReasonForRefund"] = reasonForRefund; // Store the reason for refund
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                }
                else
                {
                    // This case should ideally not happen if saga was initiated correctly
                    _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} during refund. Creating fallback.", sagaId);
                    await CreateFallbackSagaData(sagaId, orderId, "Compensated_PaymentRefunded_Recovered", "Compensated_PaymentRefunded", 
                                                 reasonForFailure: reasonForRefund, paymentConfirmationId: null, // ensure no old conf id
                                                 customData: new Dictionary<string, object> { {"RefundTransactionId", refundTransactionId}, {"ReasonForRefund", reasonForRefund} });

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish PaymentRefundedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}", 
                    paymentRefundedEvent.MessageId, orderId, sagaId);
                await _sagaStore.LogStepAsync(sagaId, typeof(PaymentRefundedEvent), StepStatus.Failed, new { Error = ex.Message, Note = "Publishing PaymentRefundedEvent failed", EventMessageId = paymentRefundedEvent.MessageId });
                
                var sagaData = await _sagaStore.LoadSagaDataAsync(sagaId);
                if (sagaData != null) {
                    sagaData.Extras["PaymentServiceStatus"] = "Failed_EventPublishError_PaymentRefunded";
                    sagaData.Extras["ErrorDetails_PaymentService"] = $"Publishing PaymentRefundedEvent failed: {ex.Message}";
                    sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                    await _sagaStore.SaveSagaDataAsync(sagaId, sagaData);
                }
                // Depending on policy, this might re-throw or the saga might need another compensation trigger.
                // For now, the error is logged and saga state updated.
            }
        }
    }
}
