using Microsoft.AspNetCore.Mvc;
using MediatR;
using System.Threading;
using System.Threading.Tasks;
using OrderService.Application.Features.Orders.Commands.CreateOrder; // Command location

namespace OrderService.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IMediator _mediator;

        public OrdersController(IMediator mediator)
        {
            _mediator = mediator ?? throw new System.ArgumentNullException(nameof(mediator));
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder(
            [FromBody] CreateOrderCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null)
            {
                return BadRequest("Command cannot be null.");
            }

            // In a real API, you might perform more validation here or use FluentValidation.
            // For example, check if command.Items is null or empty if not handled by model validation.

            try
            {
                var orderId = await _mediator.Send(command, cancellationToken);

                // Return 202 Accepted, indicating the request has been accepted for processing,
                // but the processing has not been completed. This is suitable for saga initiation.
                // Optionally, include a link to a status endpoint or the created resource ID.
                return Accepted(new { OrderId = orderId });
            }
            catch (System.ArgumentException argEx) // Example: Catch validation errors from handler/domain
            {
                return BadRequest(new { Message = argEx.Message });
            }
            // Add more specific exception handling as needed
            catch (System.Exception ex)
            {
                // Log the exception (ex) with a proper logging framework
                return StatusCode(500, "An unexpected error occurred while processing the order.");
            }
        }
    }
}
