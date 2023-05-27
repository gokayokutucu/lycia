using Dapr;
using Lycia.Dapr;
using Microsoft.AspNetCore.Mvc;

namespace Sample.Consumer.Controllers;

[ApiController]
[Route("[controller]")]
public class OrderController : ControllerBase
{
    private const string PUBSUB_NAME = "pubsub";

    private readonly ILogger<OrderController> _logger;

    public OrderController(ILogger<OrderController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    [Topic(PUBSUB_NAME, "OrderCreatedCommand")]
    public async Task HandlerAsync(OrderCreated @event,
        [FromServices] OrderCreatedEventHandler handler)
    {
        await handler.Handle(@event);
    }
}