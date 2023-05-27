using Dapr.Client;
using Lycia.Dapr;

const string PUBSUB_NAME = "pubsub";
const string TOPIC_NAME = "orders";

var product = new Product("RayBan");

//Using Dapr SDK to publish a topic

while (true)
{
    var client = new DaprClientBuilder().Build();

    CancellationTokenSource source = new CancellationTokenSource();

    CancellationToken cancellationToken = source.Token;

    await client.PublishEventAsync(PUBSUB_NAME, TOPIC_NAME, product, cancellationToken);

    Console.WriteLine("Data was sent");

    System.Threading.Thread.Sleep(5000);
}