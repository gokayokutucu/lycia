using Lycia.Infrastructure.Messaging;
using Lycia.Infrastructure.RabbitMq;
using Lycia.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging; // Required for ILogger

namespace Lycia.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds RabbitMQ publishing services to the specified IServiceCollection.
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to.</param>
        /// <param name="rabbitMqConnectionUri">Optional RabbitMQ connection URI. If null, uses default "amqp://guest:guest@localhost:5672".</param>
        /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
        public static IServiceCollection AddRabbitMqPublisher(this IServiceCollection services, string? rabbitMqConnectionUri = null)
        {
            // Register the channel provider as a singleton as it manages the connection.
            // It requires ILogger<RabbitMqConnectionProvider>.
            services.AddSingleton<IRabbitMqChannelProvider>(sp => 
                new RabbitMqConnectionProvider(
                    sp.GetService<ILogger<RabbitMqConnectionProvider>>(), // Resolve logger
                    rabbitMqConnectionUri
                )
            );

            // Register the message publisher. It depends on IRabbitMqChannelProvider and ILogger<RabbitMqMessagePublisher>.
            // Scope can be Singleton or Scoped depending on how channels are managed and if publisher holds state.
            // Given RabbitMqConnectionProvider is Singleton and GetChannel() creates new channels, Scoped or Transient is fine for publisher.
            // Let's use Scoped.
            services.AddScoped<IMessagePublisher, RabbitMqMessagePublisher>(sp =>
                new RabbitMqMessagePublisher(
                    sp.GetRequiredService<IRabbitMqChannelProvider>(),
                    sp.GetService<ILogger<RabbitMqMessagePublisher>>() // Resolve logger
                )
            );
            
            return services;
        }

        /// <summary>
        /// Adds RabbitMQ message subscription services to the specified IServiceCollection.
        /// Registers <see cref="IMessageSubscriber"/> as a Singleton.
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to.</param>
        /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
        public static IServiceCollection AddLyciaMessageSubscriber(this IServiceCollection services)
        {
            // IRabbitMqChannelProvider should already be registered as Singleton by AddRabbitMqPublisher.
            // If not, it needs to be ensured here.

            services.AddSingleton<IMessageSubscriber>(sp => 
                new RabbitMqMessageSubscriber(
                    sp.GetRequiredService<IRabbitMqChannelProvider>(),
                    sp.GetRequiredService<IServiceProvider>(), // To resolve handlers
                    sp.GetService<ILogger<RabbitMqMessageSubscriber>>() ?? throw new InvalidOperationException($"ILogger<{nameof(RabbitMqMessageSubscriber)}> not registered.")
                )
            );

            return services;
        }

        /// <summary>
        /// Adds Redis-based saga store services to the specified IServiceCollection.
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to.</param>
        /// <param name="redisConnectionString">The Redis connection string.</param>
        /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
        public static IServiceCollection AddRedisSagaStore(this IServiceCollection services, string redisConnectionString)
        {
            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new ArgumentException("Redis connection string cannot be null or whitespace.", nameof(redisConnectionString));
            }

            // Register RedisConnectionFactory as Singleton
            services.AddSingleton<IRedisConnectionFactory>(sp => 
                new RedisConnectionFactory(
                    redisConnectionString,
                    sp.GetService<ILogger<RedisConnectionFactory>>()
                )
            );

            // Register RedisSagaStore as Scoped implementation of ISagaStore
            // RedisSagaStore depends on IRedisConnectionFactory, IEventBus, ISagaIdGenerator, and ILogger.
            // IEventBus and ISagaIdGenerator are assumed to be registered elsewhere (e.g., by Lycia.Saga DI extensions or application setup).
            services.AddScoped<Lycia.Saga.Abstractions.ISagaStore, Stores.RedisSagaStore>(sp =>
                new Stores.RedisSagaStore(
                    sp.GetRequiredService<IRedisConnectionFactory>(),
                    sp.GetRequiredService<Lycia.Saga.Abstractions.IEventBus>(), 
                    sp.GetRequiredService<Lycia.Saga.Abstractions.ISagaIdGenerator>(),
                    sp.GetService<ILogger<Stores.RedisSagaStore>>()
                )
            );
            
            return services;
        }

        /// <summary>
        /// Adds RabbitMQ-backed IEventBus implementation.
        /// Registers <see cref="RabbitMqEventBus"/> as a Singleton for <see cref="Lycia.Saga.Abstractions.IEventBus"/>.
        /// Assumes IMessagePublisher is already registered (e.g., via AddRabbitMqPublisher).
        /// </summary>
        public static IServiceCollection AddRabbitMqEventBus(this IServiceCollection services)
        {
            // RabbitMqEventBus depends on IMessagePublisher.
            // IMessagePublisher is typically registered as Scoped by AddRabbitMqPublisher.
            // If IEventBus is Singleton, it will get an IMessagePublisher from the root scope if resolved directly,
            // or a scoped one if resolved within a scope. This is generally fine for stateless publishers.
            // Alternatively, RabbitMqEventBus could take IServiceProvider to resolve IMessagePublisher per call,
            // but that adds complexity. Let's keep it Singleton for now.
            services.AddSingleton<Lycia.Saga.Abstractions.IEventBus, Eventing.RabbitMqEventBus>(sp =>
                new Eventing.RabbitMqEventBus(
                    sp.GetRequiredService<IMessagePublisher>(),
                    sp.GetService<ILogger<Eventing.RabbitMqEventBus>>()
                )
            );
            return services;
        }
    }
}
