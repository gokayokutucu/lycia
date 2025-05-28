using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Lycia.Messaging.Abstractions;
using Lycia.Infrastructure.RabbitMq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Lycia.Infrastructure.Messaging
{
    public class RabbitMqMessageSubscriber : IMessageSubscriber
    {
        private readonly IRabbitMqChannelProvider _channelProvider;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RabbitMqMessageSubscriber> _logger;

        private readonly List<SubscriptionInfo> _subscriptions = new();
        private readonly List<IModel> _activeChannels = new(); // To keep track of channels for disposal
        private readonly List<string> _consumerTags = new(); // To keep track of consumer tags for cancellation

        private bool _disposed;

        private class SubscriptionInfo
        {
            public Type EventType { get; }
            public string QueueName { get; }
            public string ExchangeName { get; }
            public string RoutingKey { get; }

            public SubscriptionInfo(Type eventType, string queueName, string exchangeName, string routingKey)
            {
                EventType = eventType;
                QueueName = queueName;
                ExchangeName = exchangeName;
                RoutingKey = routingKey;
            }
        }

        public RabbitMqMessageSubscriber(
            IRabbitMqChannelProvider channelProvider,
            IServiceProvider serviceProvider,
            ILogger<RabbitMqMessageSubscriber> logger)
        {
            _channelProvider = channelProvider ?? throw new ArgumentNullException(nameof(channelProvider));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IMessageSubscriber Subscribe<TEvent>(string queueName, string exchangeName, string routingKey)
            where TEvent : class
        {
            if (string.IsNullOrEmpty(queueName))
                throw new ArgumentException("Queue name cannot be null or empty.", nameof(queueName));
            if (string.IsNullOrEmpty(exchangeName))
                throw new ArgumentException("Exchange name cannot be null or empty.", nameof(exchangeName));
            if (routingKey == null) // Routing key can be empty
                throw new ArgumentNullException(nameof(routingKey));

            _logger.LogInformation("Subscribing to event {EventType} on queue {QueueName} (Exchange: {ExchangeName}, RoutingKey: {RoutingKey})",
                typeof(TEvent).Name, queueName, exchangeName, routingKey);
            
            _subscriptions.Add(new SubscriptionInfo(typeof(TEvent), queueName, exchangeName, routingKey));
            return this;
        }

        public void StartListening()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RabbitMqMessageSubscriber));
            }

            _logger.LogInformation("Starting to listen for messages on {SubscriptionCount} subscriptions.", _subscriptions.Count);

            foreach (var subInfo in _subscriptions)
            {
                try
                {
                    var channel = _channelProvider.GetChannel();
                    _activeChannels.Add(channel); // Keep track for disposal

                    _logger.LogDebug("Declaring queue (passive): {QueueName} for event type {EventType}", subInfo.QueueName, subInfo.EventType.Name);
                    channel.QueueDeclarePassive(subInfo.QueueName); // Ensure queue exists

                    _logger.LogDebug("Binding queue {QueueName} to exchange {ExchangeName} with routing key {RoutingKey}",
                        subInfo.QueueName, subInfo.ExchangeName, subInfo.RoutingKey);
                    channel.QueueBind(queue: subInfo.QueueName, exchange: subInfo.ExchangeName, routingKey: subInfo.RoutingKey);

                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += async (model, ea) =>
                    {
                        var eventTypeName = subInfo.EventType.Name;
                        var messageId = ea.BasicProperties?.MessageId; // Get MessageId from properties if available
                        var correlationId = ea.BasicProperties?.CorrelationId; // Get CorrelationId if available

                        _logger.LogInformation(
                            "Received message for {EventTypeName} (MessageId: {MessageId}, CorrelationId: {CorrelationId}) on Queue: {QueueName}. DeliveryTag: {DeliveryTag}. Body size: {BodySize} bytes.", 
                            eventTypeName, messageId ?? "N/A", correlationId ?? "N/A", subInfo.QueueName, ea.DeliveryTag, ea.Body.Length);
                        
                        object? eventData = null; // Use object? initially for broader deserialization attempt logging
                        string messageJson = "Could not decode as UTF8";
                        try
                        {
                            var body = ea.Body.ToArray();
                            messageJson = Encoding.UTF8.GetString(body);
                            eventData = JsonSerializer.Deserialize(messageJson, subInfo.EventType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (eventData == null) // Could be valid JSON but not matching TEvent, or simply "null" JSON
                            {
                                _logger.LogError(
                                    "Failed to deserialize message to {EventTypeName} (MessageId: {MessageId}) or type mismatch. Message body: {MessageBody}", 
                                    eventTypeName, messageId ?? "N/A", messageJson);
                                channel.BasicNack(ea.DeliveryTag, false, false); 
                                return;
                            }

                            _logger.LogInformation(
                                "Successfully deserialized message to {EventTypeName} (MessageId: {MessageId}). Event Data (sample): {EventDataSample}", 
                                eventTypeName, messageId ?? "N/A", TruncateString(messageJson, 256)); // Log a sample of the event data

                            using (var scope = _serviceProvider.CreateScope())
                            {
                                var handlers = scope.ServiceProvider.GetServices(typeof(IEventHandler<>).MakeGenericType(subInfo.EventType)).ToList();
                                if (!handlers.Any())
                                {
                                    _logger.LogWarning(
                                        "No handlers found for event type {EventTypeName} (MessageId: {MessageId}). Message will be NACKed (not requeued).", 
                                        eventTypeName, messageId ?? "N/A");
                                    channel.BasicNack(ea.DeliveryTag, false, false);
                                    return;
                                }
                                
                                _logger.LogInformation(
                                    "Found {HandlerCount} handlers for event type {EventTypeName} (MessageId: {MessageId}). Invoking handlers.", 
                                    handlers.Count, eventTypeName, messageId ?? "N/A");

                                foreach (var handler in handlers)
                                {
                                    if (handler == null) continue;
                                    
                                    _logger.LogDebug(
                                        "Invoking handler {HandlerType} for event {EventTypeName} (MessageId: {MessageId})", 
                                        handler.GetType().FullName, eventTypeName, messageId ?? "N/A");
                                    
                                    // Direct cast and invoke is safer and more performant than reflection if TEvent is known at this point in the loop.
                                    // However, subInfo.EventType is Type, so reflection or dynamic is needed if TEvent is not generic here.
                                    // The existing reflection approach is fine, just adding more logging around it.
                                    var method = handler.GetType().GetMethod("HandleAsync", new[] { subInfo.EventType });
                                    if (method != null)
                                    {
                                        await (Task)method.Invoke(handler, new object[] { eventData });
                                        _logger.LogDebug(
                                            "Handler {HandlerType} completed for event {EventTypeName} (MessageId: {MessageId})", 
                                            handler.GetType().FullName, eventTypeName, messageId ?? "N/A");
                                    }
                                    else
                                    {
                                        _logger.LogError("Method HandleAsync not found on handler {HandlerType} for event type {EventType}. This should not happen.", handler.GetType().FullName, subInfo.EventType.FullName);
                                    }
                                }
                            }
                            
                            channel.BasicAck(ea.DeliveryTag, false);
                            _logger.LogInformation(
                                "Message {EventTypeName} (MessageId: {MessageId}) processed and ACKed. DeliveryTag: {DeliveryTag}", 
                                eventTypeName, messageId ?? "N/A", ea.DeliveryTag);
                        }
                        catch (JsonException jsonEx)
                        {
                            _logger.LogError(jsonEx, 
                                "JSON Deserialization error for {EventTypeName} (MessageId: {MessageId}). DeliveryTag: {DeliveryTag}. MessageBody: {MessageBody}. Message will be NACKed (not requeued).", 
                                eventTypeName, messageId ?? "N/A", ea.DeliveryTag, messageJson);
                            channel.BasicNack(ea.DeliveryTag, false, false); 
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, 
                                "Error processing message for {EventTypeName} (MessageId: {MessageId}). DeliveryTag: {DeliveryTag}. EventData (if deserialized): {@EventData}. Message will be NACKed (not requeued).", 
                                eventTypeName, messageId ?? "N/A", ea.DeliveryTag, eventData);
                            channel.BasicNack(ea.DeliveryTag, false, false);
                        }
                    };

                    string consumerTag = channel.BasicConsume(queue: subInfo.QueueName, autoAck: false, consumer: consumer);
                    _consumerTags.Add(consumerTag); // Store for cancellation on dispose
                    _logger.LogInformation("Consumer started for queue {QueueName} with consumer tag {ConsumerTag}", subInfo.QueueName, consumerTag);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start listening on queue {QueueName} for event {EventType}", subInfo.QueueName, subInfo.EventType.Name);
                    // Decide if this should stop all listening or just skip this subscription
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _logger.LogInformation("Disposing RabbitMqMessageSubscriber...");
                // Attempt to cancel consumers. This is best-effort.
                // Channels are closed via _channelProvider's disposal if it owns them, 
                // or explicitly if this class has its own management.
                // For now, assume _channelProvider.Dispose() handles underlying connection/channel closing.
                // Individual channels created here might need explicit closing if not handled by provider's Dispose.
                
                // This simple example doesn't have explicit consumer cancellation logic for each tag
                // but in a production system, you might iterate through _consumerTags and call channel.BasicCancel.
                // However, closing the channel usually suffices.

                foreach (var channel in _activeChannels)
                {
                    try
                    {
                        if (channel.IsOpen)
                        {
                            channel.Close();
                            _logger.LogDebug("Closed active channel {ChannelNumber}", channel.ChannelNumber);
                        }
                        channel.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing an active RabbitMQ channel.");
                    }
                }
                _activeChannels.Clear();
                _consumerTags.Clear();
                _logger.LogInformation("RabbitMqMessageSubscriber disposed.");
            }
            _disposed = true;
        }

        private static string TruncateString(string? str, int maxLength)
        {
            if (string.IsNullOrEmpty(str))
                return string.Empty;

            return str.Length <= maxLength ? str : str.Substring(0, maxLength) + "...";
        }
    }
}
