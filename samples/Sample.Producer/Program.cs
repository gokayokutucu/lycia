using Dapr.Client;
using Lycia.Dapr;
using Lycia.Dapr.Enums;

const string PUBSUB_NAME = "pubsub";

var orderCreated = new OrderCreated(OrderStatus.Created.ToString());

//Using Dapr SDK to publish a topic

while (true)
{
    var client = new DaprClientBuilder().Build();

    CancellationTokenSource source = new CancellationTokenSource();

    CancellationToken cancellationToken = source.Token;

    await client.PublishEventAsync(PUBSUB_NAME, "OrderCreatedCommand", orderCreated, cancellationToken);

    Console.WriteLine("Order is created");

    System.Threading.Thread.Sleep(5000);
}