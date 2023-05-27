using Dapr;
using Lycia.Dapr;
using Microsoft.AspNetCore.Mvc;

namespace Sample.Consumer.Controllers;

[ApiController]
[Route("[controller]")]
public class OrderController : ControllerBase
{
    const string PUBSUB_NAME = "pubsub";
    
    private readonly ILogger<OrderController> _logger;

    public OrderController(ILogger<OrderController> logger)
    {
        _logger = logger;
    }
    

    [HttpPost]
    [Topic(PUBSUB_NAME, "OrderCreatedCommand")]
    public Task HandlerAsync(OrderCreated orderCreated)
    {
        _logger.LogInformation("Order status is " + orderCreated.Name);

        return Task.CompletedTask;
    }
}