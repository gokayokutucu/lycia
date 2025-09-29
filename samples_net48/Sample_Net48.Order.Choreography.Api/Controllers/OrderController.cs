using Lycia.Abstractions;
using Sample_Net48.Shared.Messages.Commands;
using System.Threading.Tasks;
using System.Web.Http;

namespace Sample_Net48.Order.Choreography.Api.Controllers
{
    public class OrderController : ApiController
    {
        private readonly IEventBus _eventBus;

        public OrderController(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }
        public OrderController()
        {
            
        }
        public void Post([FromBody]CreateOrderCommand command)
        {
            _eventBus.Send(command);
        }
    }
}
