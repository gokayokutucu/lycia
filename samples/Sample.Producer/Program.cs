using Dapr.Client;
using Lycia.Dapr;
using Sample.Domain.Enums;
using Sample.Domain.Messages;

const string PUBSUB_NAME = "pubsub";

var orderCreated = new OrderCreated(OrderStatus.Created);

// Using Dapr SDK to publish a topic

while (true)
{
    var client = new DaprClientBuilder().Build();

    CancellationTokenSource source = new CancellationTokenSource();

    CancellationToken cancellationToken = source.Token;

    await client.PublishEventAsync(PUBSUB_NAME, "OrderCreated_v1", orderCreated, cancellationToken);

    Console.WriteLine("Order is created");

    System.Threading.Thread.Sleep(15000);
}