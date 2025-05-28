using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sample.PaymentService.API.Services;
using Microsoft.Extensions.Logging;

namespace Sample.PaymentService.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly PaymentProcessingService _paymentProcessingService;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(PaymentProcessingService paymentProcessingService, ILogger<PaymentsController> logger)
        {
            _paymentProcessingService = paymentProcessingService ?? throw new ArgumentNullException(nameof(paymentProcessingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public class ProcessPaymentRequest
        {
            public Guid SagaId { get; set; }
            public Guid OrderId { get; set; }
            public decimal Amount { get; set; }
            public string PaymentDetailsToken { get; set; } // Simplified for example
        }

        [HttpPost("process")]
        public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request cannot be null.");
            }
            if (request.SagaId == Guid.Empty)
            {
                 return BadRequest("SagaId must be provided.");
            }
             if (request.OrderId == Guid.Empty)
            {
                 return BadRequest("OrderId must be provided.");
            }
            if (request.Amount <= 0)
            {
                return BadRequest("Amount must be greater than zero.");
            }
             if (string.IsNullOrWhiteSpace(request.PaymentDetailsToken))
            {
                return BadRequest("PaymentDetailsToken must be provided.");
            }

            _logger.LogInformation("Received payment processing request for OrderId: {OrderId}, SagaId: {SagaId}, Amount: {Amount}", 
                request.OrderId, request.SagaId, request.Amount);

            try
            {
                // In a real scenario, paymentDetailsToken might be a more complex object or obtained securely.
                // For this example, we pass it as a string.
                bool success = await _paymentProcessingService.ProcessPaymentAsync(request.SagaId, request.OrderId, request.Amount, request.PaymentDetailsToken);
                
                if (success)
                {
                    _logger.LogInformation("Payment processing successful for OrderId: {OrderId}", request.OrderId);
                    return Ok(new { message = "Payment processing successful.", orderId = request.OrderId, sagaId = request.SagaId });
                }
                else
                {
                    _logger.LogWarning("Payment processing failed for OrderId: {OrderId}", request.OrderId);
                    // The service itself has published the failure event.
                    // The controller can return a specific status code indicating business rule failure.
                    return Conflict(new { message = "Payment processing failed.", orderId = request.OrderId, sagaId = request.SagaId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during payment processing for OrderId: {OrderId}", request.OrderId);
                return StatusCode(500, "An internal server error occurred.");
            }
        }
    }
}
