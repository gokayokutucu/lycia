using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace Sample.OrderService.IntegrationTests
{
    public class IntegrationTestFixture : IAsyncLifetime
    {
        public RabbitMqContainer RabbitMqContainer { get; private set; }
        public RedisContainer RedisContainer { get; private set; }

        public string RabbitMqBrokerUri => RabbitMqContainer.GetConnectionString();
        public string RedisConnectionString => RedisContainer.GetConnectionString();

        public IntegrationTestFixture()
        {
            // It's important to use unique names if tests might run in parallel on the same machine,
            // or rely on Testcontainers to assign random ports.
            // For RabbitMQ, default user/pass is guest/guest.
            RabbitMqContainer = new RabbitMqBuilder()
                .WithImage("rabbitmq:3-management") // Use an image that includes the management plugin for easier debugging if needed
                //.WithUsername("testuser") // Optional: custom user/pass
                //.WithPassword("testpass") // Optional: custom user/pass
                .WithName($"rabbitmq-orderservice-tests-{Guid.NewGuid().ToString().Substring(0, 8)}")
                .Build();

            RedisContainer = new RedisBuilder()
                .WithImage("redis:latest")
                .WithName($"redis-orderservice-tests-{Guid.NewGuid().ToString().Substring(0, 8)}")
                .Build();
        }

        public async Task InitializeAsync()
        {
            // Start containers in parallel
            var rabbitMqStartTask = RabbitMqContainer.StartAsync();
            var redisStartTask = RedisContainer.StartAsync();
            
            await Task.WhenAll(rabbitMqStartTask, redisStartTask);
        }

        public async Task DisposeAsync()
        {
            // Stop containers in parallel
            var rabbitMqStopTask = RabbitMqContainer.StopAsync();
            var redisStopTask = RedisContainer.StopAsync();

            await Task.WhenAll(rabbitMqStopTask, redisStopTask);
            
            // Dispose of containers
            // Not strictly necessary with await StopAsync for these specific containers, 
            // but good practice if IAsyncDisposable was directly implemented by them.
            // await RabbitMqContainer.DisposeAsync();
            // await RedisContainer.DisposeAsync();
        }
    }
}
