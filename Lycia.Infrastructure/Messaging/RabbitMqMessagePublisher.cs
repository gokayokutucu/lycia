using Lycia.Messaging.Abstractions; // For IMessagePublisher
using Lycia.Infrastructure.RabbitMq; // For IRabbitMqChannelProvider
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Lycia.Infrastructure.Messaging
{
    public class RabbitMqMessagePublisher : IMessagePublisher
    {
        private readonly IRabbitMqChannelProvider _channelProvider;
        private readonly ILogger<RabbitMqMessagePublisher> _logger;

        public RabbitMqMessagePublisher(IRabbitMqChannelProvider channelProvider, ILogger<RabbitMqMessagePublisher>? logger)
        {
            _channelProvider = channelProvider ?? throw new ArgumentNullException(nameof(channelProvider));
            _logger = logger ?? NullLogger<RabbitMqMessagePublisher>.Instance;
        }

        public Task PublishAsync<T>(string exchangeName, string routingKey, T message) where T : class
        {
            if (string.IsNullOrEmpty(exchangeName))
            {
                throw new ArgumentException("Exchange name cannot be null or empty.", nameof(exchangeName));
            }

            if (routingKey == null) // Routing key can be empty for certain exchange types (e.g., fanout)
            {
                throw new ArgumentNullException(nameof(routingKey), "Routing key cannot be null.");
            }
            
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            var messageId = (message as IMessage)?.MessageId; // Attempt to get MessageId if IMessage is implemented
            _logger.LogDebug(
                "Attempting to publish message {MessageType} with MessageId {MessageId} to Exchange: {ExchangeName}, RoutingKey: {RoutingKey}",
                message.GetType().Name, messageId, exchangeName, routingKey);

            try
            {
                using var channel = _channelProvider.GetChannel(); 

                var jsonMessage = JsonSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(jsonMessage);

                var properties = channel.CreateBasicProperties();
                properties.Persistent = true; 
                properties.ContentType = "application/json"; // Set content type

                // Optional: Add a BasicReturn handler for mandatory flag
                channel.BasicReturn += (sender, ea) => {
                    _logger.LogWarning(
                        "Message (MessageId: {ReturnedMessageId}, Type: {MessageType}) returned. ReplyCode: {ReplyCode}, ReplyText: {ReplyText}, Exchange: {Exchange}, RoutingKey: {RoutingKey}",
                        ea.BasicProperties?.MessageId, message.GetType().Name, ea.ReplyCode, ea.ReplyText, ea.Exchange, ea.RoutingKey);
                };
                
                // If message implements IMessage, use its MessageId
                if (messageId.HasValue && messageId.Value != Guid.Empty) {
                    properties.MessageId = messageId.Value.ToString();
                }


                channel.BasicPublish(
                    exchange: exchangeName,
                    routingKey: routingKey,
                    mandatory: true, 
                    basicProperties: properties,
                    body: body);

                _logger.LogInformation(
                    "Message {MessageType} with MessageId {MessageId} published to Exchange: {ExchangeName}, RoutingKey: {RoutingKey}. Body size: {BodySize} bytes.",
                    message.GetType().Name, properties.MessageId ?? "N/A", exchangeName, routingKey, body.Length);
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error publishing message {MessageType} with MessageId {MessageId} to Exchange: {ExchangeName}, RoutingKey: {RoutingKey}. Message: {@MessageObject}", 
                    message.GetType().Name, messageId ?? Guid.Empty, exchangeName, routingKey, message);
                
                throw new Exception($"Failed to publish message to RabbitMQ. Exchange: {exchangeName}, RoutingKey: {routingKey}, MessageType: {message.GetType().Name}", ex);
            }
        }
    }
}
