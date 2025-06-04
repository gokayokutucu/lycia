using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderService.Application.Features.Orders.Notifications;
using OrderService.Infrastructure.Messaging; // For RabbitMqOptions
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sample.Shared.Messages.Events;

namespace OrderService.Api.Messaging
{
    public class OrderEventRabbitMqListener : BackgroundService
    {
        private readonly RabbitMqOptions _options;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private IConnection _connection;
        private IModel _channel;
        private readonly string _queueName = "order_service_events_queue";

        public OrderEventRabbitMqListener(IOptions<RabbitMqOptions> options, IServiceScopeFactory serviceScopeFactory)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.Register(() =>
            {
                Console.WriteLine("RabbitMQ Listener is stopping.");
                _channel?.Close(); // Consider using Dispose with null-conditional
                _connection?.Close(); // Consider using Dispose with null-conditional
            });

            await StartListener(stoppingToken);
        }

        private async Task StartListener(CancellationToken stoppingToken)
        {
            var factory = new ConnectionFactory()
            {
                HostName = _options.Hostname,
                Port = _options.Port,
                UserName = _options.Username,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                DispatchConsumersAsync = true
            };

            try
            {
                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();

                _channel.ExchangeDeclare(exchange: _options.ExchangeName, type: ExchangeType.Topic, durable: true);
                _channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);

                var eventRoutingMap = new Dictionary<string, Type>
                {
                    { nameof(StockAvailableEvent), typeof(StockAvailableMediatRNotification) },
                    { nameof(StockUnavailableEvent), typeof(StockUnavailableMediatRNotification) },
                    { nameof(OrderConfirmedEvent), typeof(OrderConfirmedMediatRNotification) },
                    { nameof(OrderCreationFailedEvent), typeof(OrderCreationFailedMediatRNotification) }
                };

                foreach (var eventName in eventRoutingMap.Keys)
                {
                    _channel.QueueBind(queue: _queueName, exchange: _options.ExchangeName, routingKey: eventName);
                    Console.WriteLine($"Bound queue '{_queueName}' to exchange '{_options.ExchangeName}' with routing key '{eventName}'");
                }

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += async (model, ea) =>
                {
                    var body = ea.Body.ToArray();
                    var messageString = Encoding.UTF8.GetString(body);
                    var messageType = ea.BasicProperties.Type;

                    Console.WriteLine($"Received message. Type: {messageType}. Raw: {messageString}");

                    if (string.IsNullOrEmpty(messageType))
                    {
                        Console.WriteLine("Message type is missing. Cannot process.");
                        _channel.BasicNack(ea.DeliveryTag, false, false);
                        return;
                    }

                    try
                    {
                        using (var scope = _serviceScopeFactory.CreateScope())
                        {
                            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                            INotification notificationToPublish = null;
                            object deserializedEvent = null;

                            if (messageType == nameof(StockAvailableEvent))
                            {
                                deserializedEvent = JsonSerializer.Deserialize<StockAvailableEvent>(messageString);
                                if (deserializedEvent != null) notificationToPublish = new StockAvailableMediatRNotification((StockAvailableEvent)deserializedEvent);
                            }
                            else if (messageType == nameof(StockUnavailableEvent))
                            {
                                deserializedEvent = JsonSerializer.Deserialize<StockUnavailableEvent>(messageString);
                                if (deserializedEvent != null) notificationToPublish = new StockUnavailableMediatRNotification((StockUnavailableEvent)deserializedEvent);
                            }
                             else if (messageType == nameof(OrderConfirmedEvent))
                            {
                                deserializedEvent = JsonSerializer.Deserialize<OrderConfirmedEvent>(messageString);
                                if (deserializedEvent != null) notificationToPublish = new OrderConfirmedMediatRNotification((OrderConfirmedEvent)deserializedEvent);
                            }
                            else if (messageType == nameof(OrderCreationFailedEvent))
                            {
                                deserializedEvent = JsonSerializer.Deserialize<OrderCreationFailedEvent>(messageString);
                                if (deserializedEvent != null) notificationToPublish = new OrderCreationFailedMediatRNotification((OrderCreationFailedEvent)deserializedEvent);
                            }

                            if (notificationToPublish != null && deserializedEvent != null)
                            {
                                await mediator.Publish(notificationToPublish, stoppingToken);
                                _channel.BasicAck(ea.DeliveryTag, false);
                                Console.WriteLine($"Successfully processed and ACKed {messageType}.");
                            }
                            else
                            {
                                string reason = deserializedEvent == null ? "Deserialization returned null." : "No MediatR wrapper configured.";
                                Console.WriteLine($"{reason} for message type: {messageType}. Message will be NACKed (not requeued).");
                                _channel.BasicNack(ea.DeliveryTag, false, false);
                            }
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Console.WriteLine($"JSON Deserialization error for {messageType}: {jsonEx.Message}. Raw: {messageString}. Message will be NACKed (not requeued).");
                        _channel.BasicNack(ea.DeliveryTag, false, false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing message type {messageType}: {ex.ToString()}. Message will be NACKed (requeued for now).");
                        _channel.BasicNack(ea.DeliveryTag, false, true);
                    }
                };

                _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
                Console.WriteLine($"RabbitMQ Listener started on queue '{_queueName}'. Waiting for messages...");

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RabbitMQ Listener failed to start or encountered a critical error: {ex.ToString()}");
            }
            finally
            {
                // Consider more robust disposal, e.g. checking IsOpen before Close
                _channel?.Close(200, "Listener shutting down");
                _connection?.Close(200, "Listener shutting down");
                Console.WriteLine("RabbitMQ Listener shut down complete.");
            }
        }

        public override void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
            base.Dispose();
        }
    }
}
