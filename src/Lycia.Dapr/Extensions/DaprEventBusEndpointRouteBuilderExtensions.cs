using System.Net.Mime;
using System.Text.Json;
using Dapr.Client;
using Lycia.Dapr.EventBus;
using Lycia.Dapr.EventBus.Abstractions;
using Lycia.Dapr.Messages;
using Lycia.Dapr.Messages.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace Lycia.Dapr.Extensions;

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

        var logger = endpoints.ServiceProvider.GetRequiredService<ILogger<DaprEventBus>>();
        var eventBus = endpoints.ServiceProvider.GetRequiredService<IEventBus>();
        var daprClient = endpoints.ServiceProvider.GetRequiredService<DaprClient>();

        // Configure event bus
        logger?.LogInformation("Configuring event bus ...");
        configure?.Invoke(eventBus);

        IEndpointConventionBuilder? builder = null;
        foreach (var topic in eventBus.Topics)
        {
            logger?.LogInformation("Mapping Post for topic: {TopicKey}", topic.Key);
            builder = endpoints
                    .MapPost(topic.Key, HandleMessage)
                    .WithTopic("pubsub", topic.Key, new Dictionary<string, string>()
                    {
                        {"event-type","order-created"}
                    });
        }

        async Task HandleMessage(HttpContext context)
        {
            logger?.LogInformation("Request path: {RequestPath}", context.Request.Path);
            //   Get handlers
            var handler = GetHandlersForRequest(context.Request.Path);
            if (handler is null) return;

            // Get event type
            var eventType = GetEventType(handler);

            // Get event
            var @event = await GetEventAsync(context, eventType, daprClient?.JsonSerializerOptions);

            // Process handlers
            var errorOccurred = false;
            //foreach (var handler in handlers!)
            //{
            try
            {
                if (@event != null) await handler.Handle(@event);
            }
            catch (Exception e)
            {
                logger?.LogInformation("Handler threw exception: {Message}", e);
                errorOccurred = true;
            }
            //}
        }

        IEventHandler? GetHandlersForRequest(string path)
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

        async Task<Event?> GetEventAsync(HttpContext context,
            Type? eventType, JsonSerializerOptions? serializerOptions)
        {
            // Check content type
            if (!string.Equals(context.Request.ContentType, MediaTypeNames.Application.Json,
                    StringComparison.Ordinal))
            {
                logger?.LogInformation("Unsupported Content-Type header: {ContentType}",
                    context.Request.ContentType);
                return null;
            }

            // Get event
            try
            {
                var value = await JsonSerializer.DeserializeAsync(context.Request.Body, eventType!, serializerOptions);
                return (Event)value!;
            }
            catch (Exception e) when (e is JsonException || e is ArgumentNullException || e is NotSupportedException)
            {
                logger?.LogInformation("Unable to deserialize event from request '{RequestPath}': {Message}",
                    context.Request.Path, e.Message);
                return null;
            }
        }

        return new DaprEventBusEndpointConventionBuilder(builder!);
    }
}