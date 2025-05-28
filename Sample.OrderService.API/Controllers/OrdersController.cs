using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sample.OrderService.API.Events; // For OrderDetailsDto
using Sample.OrderService.API.Services; // For OrderCreationService

namespace Sample.OrderService.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderCreationService _orderCreationService;

        public OrdersController(OrderCreationService orderCreationService)
        {
            _orderCreationService = orderCreationService ?? throw new ArgumentNullException(nameof(orderCreationService));
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] OrderDetailsDto orderDetails)
        {
            if (orderDetails == null)
            {
                return BadRequest("Order details cannot be null.");
            }

            try
            {
                var orderId = await _orderCreationService.CreateOrderAsync(orderDetails);
                // Return a 201 Created response with the location of the new resource (optional)
                // and the orderId in the response body.
                return CreatedAtAction(nameof(GetOrderById), new { orderId = orderId }, new { orderId = orderId });
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error during order creation: {ex.Message}");
                return StatusCode(500, "An internal server error occurred while creating the order.");
            }
        }

        // Example of a GetOrderById endpoint - not required by the task but good for RESTful design
        [HttpGet("{orderId}")]
        public IActionResult GetOrderById(Guid orderId)
        {
            // In a real application, you would retrieve and return the order details.
            // For this example, we'll just return a placeholder.
            if (orderId == Guid.Empty)
            {
                return NotFound();
            }
            return Ok(new { orderId = orderId, message = "Order details would be here." });
        }
    }
}
