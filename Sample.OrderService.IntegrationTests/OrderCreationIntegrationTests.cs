using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lycia.Saga; // For SagaData
using Microsoft.AspNetCore.Mvc.Testing;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sample.OrderService.API.Events; // For OrderDetailsDto, OrderCreatedEvent
using Sample.OrderService.API.Models; // For OrderSagaData
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions; // For ITestOutputHelper

namespace Sample.OrderService.IntegrationTests
{
    public class OrderCreationIntegrationTests : IClassFixture<OrderServiceAppFactory<Sample.OrderService.API.Program>> // Use the specific Program for minimal API
    {
        private readonly OrderServiceAppFactory<Sample.OrderService.API.Program> _factory;
        private readonly HttpClient _client;
        private readonly ITestOutputHelper _output; // For Xunit logging

        // Testcontainer connection strings are accessed via the factory's fixture
        private readonly string _rabbitMqBrokerUri;
        private readonly string _redisConnectionString;

        public OrderCreationIntegrationTests(OrderServiceAppFactory<Sample.OrderService.API.Program> factory, ITestOutputHelper output)
        {
            _factory = factory;
            _client = _factory.CreateClient();
            _output = output;

            // Access connection strings from the fixture managed by the factory
            _rabbitMqBrokerUri = _factory.Fixture.RabbitMqBrokerUri;
            _redisConnectionString = _factory.Fixture.RedisConnectionString;
        }

        // Helper class for deserializing API response
        private class CreateOrderResponse
        {
            public Guid OrderId { get; set; }
            public Guid SagaId { get; set; }
        }


        [Fact]
        public async Task CreateOrder_WhenValidRequest_ShouldPublishOrderCreatedEventAndSaveInitialSagaState()
        {
            // Arrange
            var orderIdFromApi = Guid.Empty;
            var sagaIdFromApi = Guid.Empty;

            var orderDetails = new OrderDetailsDto
            {
                CustomerId = "customer-123",
                Items = new List<OrderItemDto>
                {
                    new OrderItemDto { ProductId = "product-abc", Quantity = 2, UnitPrice = 10.50m },
                    new OrderItemDto { ProductId = "product-xyz", Quantity = 1, UnitPrice = 25.00m }
                },
                TotalAmount = 46.00m
            };

            // RabbitMQ Consumer Setup
            var factory = new ConnectionFactory { Uri = new Uri(_rabbitMqBrokerUri) };
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            var exchangeName = "saga_events_exchange";
            var queueName = "inventory_service_order_created_q_test"; // Test-specific queue
            var routingKey = "order.created";

            channel.ExchangeDeclare(exchange: exchangeName, type: ExchangeType.Topic, durable: true);
            channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            channel.QueueBind(queue: queueName, exchange: exchangeName, routingKey: routingKey);

            var messageReceivedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            OrderCreatedEvent? receivedEvent = null;

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    _output.WriteLine($"[RabbitMQ Consumer] Received message: {message}");
                    receivedEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(message, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    messageReceivedTcs.TrySetResult(message); // Signal message receipt
                    channel.BasicAck(ea.DeliveryTag, false); // Acknowledge the message
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"[RabbitMQ Consumer] Error processing message: {ex.Message}");
                    messageReceivedTcs.TrySetException(ex);
                    channel.BasicNack(ea.DeliveryTag, false, false); // Nack without requeue
                }
            };
            string consumerTag = channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
            _output.WriteLine($"[RabbitMQ Consumer] Consumer started on queue '{queueName}' with tag '{consumerTag}'. Waiting for message...");


            // Act
            _output.WriteLine("[Test API Call] Sending POST request to /api/orders");
            HttpResponseMessage response = await _client.PostAsJsonAsync("/api/orders", orderDetails);


            // Assert (Part 1 - API Response & Event)
            response.EnsureSuccessStatusCode(); // Throws if not 2xx
            _output.WriteLine($"[Test API Call] API response status: {response.StatusCode}");
            
            var apiResponse = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
            apiResponse.Should().NotBeNull();
            orderIdFromApi = apiResponse!.OrderId;
            sagaIdFromApi = apiResponse.SagaId;
            _output.WriteLine($"[Test API Call] API response - OrderId: {orderIdFromApi}, SagaId: {sagaIdFromApi}");

            orderIdFromApi.Should().NotBeEmpty();
            sagaIdFromApi.Should().NotBeEmpty();

            // Wait for RabbitMQ message with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10)); // 10-second timeout
            var completedTask = await Task.WhenAny(messageReceivedTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _output.WriteLine("[RabbitMQ Consumer] Timeout waiting for message.");
            }
            Assert.True(completedTask == messageReceivedTcs.Task, "RabbitMQ message was not received within the timeout period.");
            
            _output.WriteLine("[RabbitMQ Consumer] Message processing completed by TCS.");
            receivedEvent.Should().NotBeNull();
            receivedEvent!.SagaId.Should().Be(sagaIdFromApi);
            receivedEvent.OrderId.Should().Be(orderIdFromApi);
            receivedEvent.OrderDetails.Should().BeEquivalentTo(orderDetails, options => options.ComparingByMembers<OrderDetailsDto>());
             _output.WriteLine($"[RabbitMQ Consumer] Verified OrderCreatedEvent - OrderId: {receivedEvent.OrderId}, SagaId: {receivedEvent.SagaId}");

            // Clean up consumer
            channel.BasicCancel(consumerTag);


            // Assert (Part 2 - Redis Saga State)
            _output.WriteLine("[Redis Check] Connecting to Redis to verify saga state...");
            var redis = await ConnectionMultiplexer.ConnectAsync(_redisConnectionString);
            var db = redis.GetDatabase();
            var sagaDataKey = $"saga:{sagaIdFromApi}:data";
            
            RedisValue sagaDataJson = await db.StringGetAsync(sagaDataKey);
            sagaDataJson.HasValue.Should().BeTrue($"because SagaData should be saved for SagaId {sagaIdFromApi}");
            _output.WriteLine($"[Redis Check] Retrieved SagaData JSON for key '{sagaDataKey}': {sagaDataJson}");

            var sagaData = JsonSerializer.Deserialize<OrderSagaData>(sagaDataJson.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            sagaData.Should().NotBeNull();
            sagaData!.Extras.Should().ContainKey("OverallStatus").WhoseValue.Should().BeEquivalentTo("OrderCreated_AwaitingStockReservation");
            sagaData.Extras.Should().ContainKey("OrderId").WhoseValue.Should().BeEquivalentTo(orderIdFromApi.ToString());
            sagaData.Extras.Should().ContainKey("SagaType").WhoseValue.Should().BeEquivalentTo("OrderPlacementSaga");
            sagaData.Extras.Should().ContainKey("OrderServiceStatus").WhoseValue.Should().BeEquivalentTo("Completed");
            _output.WriteLine($"[Redis Check] Verified SagaData for SagaId: {sagaIdFromApi}");
        }
    }
}
