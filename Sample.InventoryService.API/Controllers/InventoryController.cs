using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sample.InventoryService.API.Dtos;
using Sample.InventoryService.API.Services;
using Microsoft.Extensions.Logging;

namespace Sample.InventoryService.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryController : ControllerBase
    {
        private readonly StockReservationService _stockReservationService;
        private readonly ILogger<InventoryController> _logger;

        public InventoryController(StockReservationService stockReservationService, ILogger<InventoryController> logger)
        {
            _stockReservationService = stockReservationService ?? throw new ArgumentNullException(nameof(stockReservationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public class ReserveStockRequest
        {
            public Guid SagaId { get; set; }
            public Guid OrderId { get; set; }
            public List<OrderItemDto> Items { get; set; }
        }

        [HttpPost("reserve")]
        public async Task<IActionResult> ReserveStock([FromBody] ReserveStockRequest request)
        {
            if (request == null || request.Items == null || request.Items.Count == 0)
            {
                return BadRequest("Request, items, or item list cannot be null or empty.");
            }
            if (request.SagaId == Guid.Empty)
            {
                 return BadRequest("SagaId must be provided.");
            }
             if (request.OrderId == Guid.Empty)
            {
                 return BadRequest("OrderId must be provided.");
            }

            _logger.LogInformation("Received stock reservation request for OrderId: {OrderId}, SagaId: {SagaId}", request.OrderId, request.SagaId);

            try
            {
                bool success = await _stockReservationService.ReserveStockAsync(request.SagaId, request.OrderId, request.Items);
                if (success)
                {
                    _logger.LogInformation("Stock reservation successful for OrderId: {OrderId}", request.OrderId);
                    return Ok(new { message = "Stock reservation successful.", orderId = request.OrderId, sagaId = request.SagaId });
                }
                else
                {
                    _logger.LogWarning("Stock reservation failed for OrderId: {OrderId}", request.OrderId);
                    // The service itself has published the failure event.
                    // The controller can return a specific status code indicating business rule failure.
                    return Conflict(new { message = "Stock reservation failed.", orderId = request.OrderId, sagaId = request.SagaId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during stock reservation for OrderId: {OrderId}", request.OrderId);
                return StatusCode(500, "An internal server error occurred.");
            }
        }
    }
}
