using System.Text.Json;
using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Lycia.Dapr.EventBus.Abstractions;
using Microsoft.Extensions.Options;

namespace Lycia.Dapr.EventBus.Sns;

public class AmazonSnsEventPublisher : IEventPublisher
{
    private readonly IOptions<AmazonSnsEventPublisherOptions> _options;

    public AmazonSnsEventPublisher(
        IOptions<AmazonSnsEventPublisherOptions> options)
    {
        _options = options;
    }

    public async Task PublishEventAsync<TEvent>(TEvent @event,CancellationToken cancellationToken)
    {
        var endpoint = RegionEndpoint.GetBySystemName(_options.Value.Region);
        var client = new AmazonSimpleNotificationServiceClient(endpoint);
        Type eventType = typeof(TEvent);
        var createTopicRequest = new CreateTopicRequest(eventType.FullName.ToLower().Replace('.', '-'));
        CreateTopicResponse createTopicResponse = await client.CreateTopicAsync(createTopicRequest,cancellationToken);
        string topicArn = createTopicResponse.TopicArn;

        string message = JsonSerializer.Serialize(@event);
        var publishRequest = new PublishRequest(topicArn, message);
        await client.PublishAsync(publishRequest,cancellationToken);
    }
}