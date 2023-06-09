using Amazon.SQS;
using Amazon.SQS.Model;
using Lycia.Dapr.EventBus.Abstractions;
using Lycia.Dapr.Messages.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Lycia.Dapr.EventBus.Sqs;

public class AmazonSqsEventSubscriber:IEventSubscriber
{
    private readonly IAmazonSQS _sqsClient;
    private readonly IOptions<AmazonSqsEventSubscriberOptions> _options;
    public AmazonSqsEventSubscriber(IAmazonSQS sqsClient, IOptions<AmazonSqsEventSubscriberOptions> options)
    {
        _sqsClient = sqsClient;
        _options = options;
    }

    public async Task SubscribeAsync<TEvent,TEventHandler>(TEventHandler handler,CancellationToken cancellationToken)
        where TEvent : IEvent
        where TEventHandler : IEventHandler<TEvent>
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = _options.Value.Url,
            MaxNumberOfMessages = _options.Value.MaxNumberOfMessages,
            WaitTimeSeconds = _options.Value.WaitTimeSeconds
        };

        while (true)
        {
            var response = await _sqsClient.ReceiveMessageAsync(request,cancellationToken);

            foreach (var message in response.Messages)
            {
                var messageBody = message.Body;
                var @event = JsonConvert.DeserializeObject<TEvent>(messageBody);
                await handler.Handle(@event);
                await _sqsClient.DeleteMessageAsync(_options.Value.Url, message.ReceiptHandle,cancellationToken);
            }
        }
    }
}