using Dapr.Client;
using Lycia.Dapr;
using Lycia.Dapr.Messages;
using Lycia.Dapr.Messages.Abstractions;

namespace Sample.Consumer.Extensions;

public static class DaprEventBusEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps endpoints that will handle requests for DaprEventBus subscriptions.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder" />.</param>
    /// <param name="configure">Used to subscribe to events with event handlers.</param>
    /// <returns>An <see cref="DaprEventBusEndpointConventionBuilder"/> for endpoints associated with DaprEventBus subscriptions.</returns>
    public static DaprEventBusEndpointConventionBuilder MapDaprEventBus(this IEndpointRouteBuilder endpoints,
        Action<IEventBus?>? configure = null)
    {
        if (endpoints is null)
            throw new ArgumentNullException(nameof(endpoints));

        var logger = endpoints.ServiceProvider.GetService<ILogger<DaprEventBus>>();
        var eventBus = endpoints.ServiceProvider.GetService<IEventBus>();
        var daprClient = endpoints.ServiceProvider.GetService<DaprClient>();

        IEndpointConventionBuilder? builder = null;
        foreach (var topic in eventBus.Topics)
        {
            logger?.LogInformation("Mapping Post for topic: {TopicKey}", topic.Key);
            builder = endpoints.MapPost(topic.Key, HandleMessage)
                .WithTopic("pubsub", topic.Key);
        }

        async Task HandleMessage(HttpContext context)
        {
            // Get handlers
            var handlers = new List<IEventHandler> { new OrderCreatedEventHandler() };
            var handler1 = handlers!.FirstOrDefault();
            if (handler1 == null) return;

            // Get event type
            var eventType = GetEventType(handler1);

            // Get event
            var @event = new List<IEvent> { new OrderCreated() };

            // Process handlers
            var errorOccurred = false;
            foreach (var handler in handlers!)
            {
                try
                {
                    await handler.Handle(@event.First());
                }
                catch (Exception e)
                {
                    logger?.LogInformation("Handler threw exception: {Message}", e);
                    errorOccurred = true;
                }
            }
        }

        List<IEventHandler>? GetHandlersForRequest(string path)
        {
            var topic = path.Substring(path.IndexOf("/", StringComparison.Ordinal) + 1);
            logger?.LogInformation("Topic for request: {Topic}", topic);

            if (eventBus.Topics.TryGetValue(topic, out var handlers))
                return handlers;
            return null;
        }

        Type? GetEventType(IEventHandler handler)
        {
            var eventType = handler.GetType().BaseType?.GenericTypeArguments[0];
            if (eventType != null) return eventType;
            return null;
        }

        return new DaprEventBusEndpointConventionBuilder(builder!);
    }
}