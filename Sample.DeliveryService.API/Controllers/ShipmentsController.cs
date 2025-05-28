using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sample.DeliveryService.API.Services;
using Microsoft.Extensions.Logging;

namespace Sample.DeliveryService.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShipmentsController : ControllerBase
    {
        private readonly ShipmentSchedulingService _shipmentSchedulingService;
        private readonly ILogger<ShipmentsController> _logger;

        public ShipmentsController(ShipmentSchedulingService shipmentSchedulingService, ILogger<ShipmentsController> logger)
        {
            _shipmentSchedulingService = shipmentSchedulingService ?? throw new ArgumentNullException(nameof(shipmentSchedulingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public class ScheduleShipmentRequest
        {
            public Guid SagaId { get; set; }
            public Guid OrderId { get; set; }
            public object ShippingAddressDetails { get; set; } // Simplified for example, could be a complex object
        }

        [HttpPost("schedule")]
        public async Task<IActionResult> ScheduleShipment([FromBody] ScheduleShipmentRequest request)
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
            if (request.ShippingAddressDetails == null)
            {
                return BadRequest("ShippingAddressDetails must be provided.");
            }

            _logger.LogInformation("Received shipment scheduling request for OrderId: {OrderId}, SagaId: {SagaId}", 
                request.OrderId, request.SagaId);

            try
            {
                bool success = await _shipmentSchedulingService.ScheduleShipmentAsync(request.SagaId, request.OrderId, request.ShippingAddressDetails);
                
                if (success)
                {
                    _logger.LogInformation("Shipment scheduling successful for OrderId: {OrderId}", request.OrderId);
                    return Ok(new { message = "Shipment scheduling successful.", orderId = request.OrderId, sagaId = request.SagaId });
                }
                else
                {
                    _logger.LogWarning("Shipment scheduling failed for OrderId: {OrderId}", request.OrderId);
                    // The service itself has published the failure event.
                    return Conflict(new { message = "Shipment scheduling failed.", orderId = request.OrderId, sagaId = request.SagaId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during shipment scheduling for OrderId: {OrderId}", request.OrderId);
                return StatusCode(500, "An internal server error occurred.");
            }
        }
    }
}
