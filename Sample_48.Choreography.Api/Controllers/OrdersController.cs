using Lycia.Saga.Abstractions;
using Sample.Shared.Messages.Commands;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;

namespace Sample_48.Choreography.Api.Controllers
{
    public class OrdersController : ApiController
    {
        private readonly IEventBus _eventBus;
        public OrdersController(IEventBus eventBus) => _eventBus = eventBus;

        [HttpPost]
        public async Task<IHttpActionResult> Post([FromBody] CreateOrderCommand command)
        {
            await _eventBus.Send(command);
            return StatusCode(HttpStatusCode.Accepted);
        }
    }
}
