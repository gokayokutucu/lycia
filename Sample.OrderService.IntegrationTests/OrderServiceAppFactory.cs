using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sample.OrderService.IntegrationTests
{
    public class OrderServiceAppFactory : WebApplicationFactory<Sample.OrderService.API.Program>
    {
        private readonly IntegrationTestFixture _fixture;

        public OrderServiceAppFactory(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Remove existing IHostedService registrations for RabbitMQ subscribers if they auto-start,
            // to prevent them from interfering with test-specific consumers or connecting before Testcontainers are ready.
            // This is a common pattern if your main Program.cs registers and starts subscribers.
            // builder.ConfigureServices(services =>
            // {
            //     var hostedServices = services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToList();
            //     foreach (var serviceDescriptor in hostedServices)
            //     {
            //         // Example: Remove a specific subscriber if you know its implementation type
            //         // if (serviceDescriptor.ImplementationType == typeof(YourRabbitMqSubscriberService))
            //         // {
            //         //     services.Remove(serviceDescriptor);
            //         // }
            //     }
            // });

            // Configure app configuration for Testcontainers
            builder.ConfigureAppConfiguration((context, conf) =>
            {
                // Add or replace connection strings
                // These keys should match what your application expects in appsettings.json or environment variables.
                var testConfig = new Dictionary<string, string?>
                {
                    // Assuming Lycia's AddRabbitMqPublisher and AddRedisSagaStore take direct connection strings if IConfiguration isn't used,
                    // or if they look for specific keys. The current DI extensions for these take the string directly.
                    // So, we need to ensure these are passed to those AddXyz methods.
                    // This can be done by overriding the DI registrations in ConfigureTestServices below.
                    // For now, let's add them to configuration in case any component tries to read them from IConfiguration.
                    { "ConnectionStrings:RabbitMq", _fixture.RabbitMqBrokerUri },
                    { "ConnectionStrings:Redis", _fixture.RedisConnectionString },

                    // If your AddXyz methods in Program.cs use IConfiguration.GetConnectionString("RabbitMq") etc.,
                    // then the above is sufficient.
                    // If they use hardcoded defaults or specific config variables, you might need to adjust.
                    // Example: builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"
                    // The `?? "localhost:6379"` part means we need to ensure "ConnectionStrings:Redis" is set.
                };
                conf.AddInMemoryCollection(testConfig);
            });

            // Configure services to use Testcontainer connection strings directly when setting up Lycia services
            builder.ConfigureServices(services =>
            {
                // Ensure Lycia DI extensions use the Testcontainer connection strings.
                // This might involve removing default registrations and adding them again with new URIs.
                // The existing DI extensions `AddRabbitMqPublisher`, `AddRabbitMqEventBus`, `AddRedisSagaStore`
                // accept connection strings as parameters.
                // We need to ensure the main `Program.cs` uses IConfiguration to pass these, or we override them here.

                // Let's assume Program.cs uses IConfiguration for these.
                // If `builder.Configuration.GetConnectionString("Redis")` is used in Program.cs,
                // the ConfigureAppConfiguration above should be enough.

                // If direct overriding of DI is needed (e.g., if Program.cs hardcodes defaults when IConfiguration values are missing):
                // This is more robust if you want to be absolutely sure.
                // 1. Remove existing registrations if they were added with default/different values.
                // This can be tricky as AddXyz methods might register multiple services.
                // A simpler approach for tests is often to ensure the IConfiguration is correctly seeded,
                // and the main Program.cs uses IConfiguration for these settings.

                // For this test, we'll rely on the ConfigureAppConfiguration setting the correct values
                // and assume Program.cs uses IConfiguration.GetConnectionString("RabbitMq") and GetConnectionString("Redis")
                // for the Lycia DI extensions (`AddRabbitMqPublisher` and `AddRedisSagaStore`).
                // The `?? "localhost:xxxx"` fallback in Program.cs will be overridden by the values we set in ConfigureAppConfiguration.
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Example: Ensure a specific environment for tests if needed
            // builder.UseEnvironment("IntegrationTests");
            return base.CreateHost(builder);
        }
    }
}
