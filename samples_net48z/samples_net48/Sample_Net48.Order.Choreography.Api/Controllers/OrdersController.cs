using Lycia.Saga.Abstractions;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;

namespace Sample_Net48.Order.Choreography.Api.Controllers
{
    public class OrdersController : ApiController
    {
        private readonly IEventBus _eventBus;

        public OrdersController(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }
        // POST api/values
        public async Task<IHttpActionResult> Post([FromBody] Models.CreateOrderCommand command)
        {
            await _eventBus.Send(command);
            return StatusCode(HttpStatusCode.Accepted);
        }
    }
}
