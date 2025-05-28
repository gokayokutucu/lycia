using System;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions;
using Lycia.Saga; // For StepStatus
using Lycia.Saga.Abstractions; // For ISagaStore
using Sample.PaymentService.API.Dtos.IncomingStock; // For StockReservedEventDto
using Sample.PaymentService.API.Models; // For PaymentSagaData
using Sample.PaymentService.API.Services;
using Microsoft.Extensions.Logging;

namespace Sample.PaymentService.API.EventHandlers
{
    public class StockReservedEventHandler : IEventHandler<StockReservedEventDto>
    {
        private readonly PaymentProcessingService _paymentProcessingService;
        private readonly ISagaStore _sagaStore;
        private readonly ILogger<StockReservedEventHandler> _logger;

        public StockReservedEventHandler(
            PaymentProcessingService paymentProcessingService,
            ISagaStore sagaStore, // Injected ISagaStore
            ILogger<StockReservedEventHandler> logger)
        {
            _paymentProcessingService = paymentProcessingService ?? throw new ArgumentNullException(nameof(paymentProcessingService));
            _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore)); // Store injected ISagaStore
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(StockReservedEventDto anEvent)
        {
            _logger.LogInformation(
                "Handling StockReservedEvent (MessageId: {MessageId}) for OrderId: {OrderId}, SagaId: {SagaId}. ItemsReservedCount: {ItemsReservedCount}", 
                anEvent.MessageId, anEvent.OrderId, anEvent.SagaId, anEvent.ItemsReserved?.Count ?? 0);

            // Log "Processing" step for this handler
            await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(StockReservedEventDto), StepStatus.Processing, new { anEvent.OrderId, EventMessageId = anEvent.MessageId });

            var sagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId);
            if (sagaData != null)
            {
                sagaData.Extras["OverallStatus"] = "PaymentProcessing";
                sagaData.Extras["PaymentServiceStatus"] = "Processing_StockReservedEvent";
                sagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogInformation("SagaData updated to 'PaymentProcessing' for SagaId: {SagaId}", anEvent.SagaId);
            }
            else
            {
                _logger.LogCritical("SagaData is NULL for SagaId: {SagaId} when handling StockReservedEvent. This is unexpected.", anEvent.SagaId);
                sagaData = new PaymentSagaData // Use the concrete type
                {
                    Extras = 
                    {
                        ["OrderId"] = anEvent.OrderId.ToString(),
                        ["SagaType"] = "OrderPlacementSaga", // Assuming same saga type
                        ["OverallStatus"] = "PaymentProcessing_Recovered", // Special status
                        ["PaymentServiceStatus"] = "Processing_StockReservedEvent",
                        ["Error"] = "SagaData was missing, recovered in PaymentService StockReservedEventHandler.",
                        ["LastUpdatedAt"] = DateTime.UtcNow.ToString("o")
                    }
                };
                // CreatedAt might be set by OrderService, or can be set here if it's the first time PaymentService sees this SagaId
                if (!sagaData.Extras.ContainsKey("CreatedAt")) sagaData.Extras["CreatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, sagaData);
                _logger.LogWarning("Created and saved new SagaData for SagaId: {SagaId} due to missing state.", anEvent.SagaId);
            }

            // Simulate fetching payment details for the order
            var (amount, paymentToken) = await GetPaymentDetailsForOrderAsync(anEvent.OrderId);

            if (amount <= 0 || paymentToken == null)
            {
                _logger.LogError(
                    "Failed to retrieve valid payment details for OrderId: {OrderId}, SagaId: {SagaId} from StockReservedEvent (MessageId: {MessageId}). Cannot process payment.", 
                    anEvent.OrderId, anEvent.SagaId, anEvent.MessageId);
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(StockReservedEventDto), StepStatus.Failed, new { Error = "Failed to retrieve payment details.", EventMessageId = anEvent.MessageId });
                // Update SagaData to reflect this specific failure
                var currentSagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId) ?? sagaData; // Use recently created if it was null
                currentSagaData.Extras["OverallStatus"] = "Failed_PaymentProcessing";
                currentSagaData.Extras["PaymentServiceStatus"] = "Failed_PaymentDetailsRetrieval";
                currentSagaData.Extras["ErrorDetails_StockReservedEventHandler"] = $"SagaId: {anEvent.SagaId}, OrderId: {anEvent.OrderId}, Failed to retrieve payment details.";
                currentSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, currentSagaData);
                return;
            }

            try
            {
                _logger.LogInformation(
                    "Calling PaymentProcessingService.ProcessPaymentAsync for OrderId: {OrderId}, SagaId: {SagaId}, Amount: {Amount}", 
                    anEvent.OrderId, anEvent.SagaId, amount);
                // PaymentProcessingService will handle its own step logging and SagaData updates related to payment events
                bool paymentResult = await _paymentProcessingService.ProcessPaymentAsync(anEvent.SagaId, anEvent.OrderId, amount, paymentToken);
                
                // This handler's main job is to trigger payment processing. The outcome (success/failure) is handled by PaymentProcessingService.
                _logger.LogInformation(
                    "PaymentProcessingService call completed for OrderId: {OrderId}, SagaId: {SagaId}. Result: {PaymentResult}", 
                    anEvent.OrderId, anEvent.SagaId, paymentResult);
                
                // Log "Completed" step for this handler's specific task of delegating to PaymentProcessingService
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(StockReservedEventDto), StepStatus.Completed, new { Note = "Processing delegated to PaymentProcessingService", PaymentInitiated = paymentResult, EventMessageId = anEvent.MessageId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error during payment processing delegation for OrderId: {OrderId}, SagaId: {SagaId} triggered by StockReservedEvent (MessageId: {MessageId}).", 
                    anEvent.OrderId, anEvent.SagaId, anEvent.MessageId);
                await _sagaStore.LogStepAsync(anEvent.SagaId, typeof(StockReservedEventDto), StepStatus.Failed, new { Error = ex.Message, Note = "Exception in StockReservedEventHandler when calling PaymentProcessingService", EventMessageId = anEvent.MessageId });
                
                var currentSagaData = await _sagaStore.LoadSagaDataAsync(anEvent.SagaId) ?? sagaData;
                currentSagaData.Extras["OverallStatus"] = "Failed_PaymentProcessing";
                currentSagaData.Extras["PaymentServiceStatus"] = "Failed_StockReservedEventHandlerError";
                currentSagaData.Extras["ErrorDetails_StockReservedEventHandler"] = $"SagaId: {anEvent.SagaId}, OrderId: {anEvent.OrderId}, Exception: {ex.Message}";
                currentSagaData.Extras["LastUpdatedAt"] = DateTime.UtcNow.ToString("o");
                await _sagaStore.SaveSagaDataAsync(anEvent.SagaId, currentSagaData);
                throw; 
            }
        }

        private async Task<(decimal Amount, object PaymentToken)> GetPaymentDetailsForOrderAsync(Guid orderId)
        {
            await Task.Delay(50); 
            if (orderId == Guid.Empty) return (0m, null);
            return (100.00m, "valid_token_123"); 
        }
    }
}
