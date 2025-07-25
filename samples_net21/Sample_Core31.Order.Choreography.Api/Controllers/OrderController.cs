using Lycia.Saga.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Sample_Net21.Shared.Messages.Commands;
using System.Threading.Tasks;

namespace Sample_Core31.Order.Choreography.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class OrderController : ControllerBase
    {
        private readonly IEventBus _eventBus;

        public OrderController(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] CreateOrderCommand command)
        {
            await _eventBus.Send(command);
            return Ok();
        }
    }
}
