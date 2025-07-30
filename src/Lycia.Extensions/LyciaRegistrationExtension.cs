using System.Reflection;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Listener;
using Lycia.Extensions.Stores;
using Lycia.Infrastructure.Compensating;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Stores;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Common;
using Lycia.Saga.Extensions;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Lycia.Saga.Configurations;



#if NETSTANDARD2_0
using Autofac;
#else
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#endif

namespace Lycia.Extensions;

public static class LyciaRegistrationExtension
{
#if NETSTANDARD2_0
    public static void AddLycia(this ContainerBuilder builder, System.Configuration.Configuration? configuration = null, Type? sagaType = null)
    {


        var eventBusProvider = configuration?.AppSettings.Settings["Lycia:EventBus:Provider"]?.Value ?? "RabbitMQ";
        var eventStoreProvider = configuration?.AppSettings.Settings["Lycia:EventStore:Provider"]?.Value ?? "Redis";

        var appId = configuration?.AppSettings.Settings["ApplicationId"]?.Value
                    ?? throw new InvalidOperationException("ApplicationId is not configured.");

        var ttlSeconds = int.TryParse(configuration?.AppSettings.Settings["Lycia:CommonTTL"]?.Value, out var parsedTtl) && parsedTtl > 0
            ? parsedTtl
            : Constants.Ttl;

        var logMaxRetryCount = int.TryParse(configuration?.AppSettings.Settings["Lycia:EventStore:LogMaxRetryCount"]?.Value, out var parsedLogRetryMaxCount) && parsedLogRetryMaxCount > 0
            ? parsedLogRetryMaxCount
            : Constants.LogMaxRetryCount;

        builder.RegisterType<DefaultSagaIdGenerator>().As<ISagaIdGenerator>().InstancePerLifetimeScope();
        builder.RegisterType<SagaDispatcher>().As<ISagaDispatcher>().InstancePerLifetimeScope();
        builder.RegisterType<SagaCompensationCoordinator>().As<ISagaCompensationCoordinator>().InstancePerLifetimeScope();





        if (eventStoreProvider == "Redis")
        {
            var conn = configuration?.AppSettings.Settings["Lycia:EventStore:ConnectionString"]?.Value
                       ?? throw new InvalidOperationException("Lycia:EventStore:ConnectionString is not configured.");
            builder.RegisterInstance(ConnectionMultiplexer.Connect(conn)).As<IConnectionMultiplexer>().SingleInstance();
            builder.Register(ctx => ctx.Resolve<IConnectionMultiplexer>().GetDatabase()).As<IDatabase>().InstancePerLifetimeScope();
        }


        var queueTypeMap = SagaHandlerRegistrationExtensions.DiscoverQueueTypeMap(appId, sagaType?.Assembly ?? Assembly.GetCallingAssembly());


        if (eventBusProvider == "RabbitMQ")
        {
            builder.Register(ctx =>
            {
                var conn = configuration?.AppSettings.Settings["Lycia:EventBus:ConnectionString"]?.Value
                           ?? throw new InvalidOperationException("Lycia:EventBus:ConnectionString is not configured.");
                // Logger implementation? (Autofac ile resolve edeceğin logger ekle!)
                var logger = ctx.ResolveOptional<ILogger<RabbitMqEventBus>>();

                var eventBusOptions = new EventBusOptions
                {
                    ApplicationId = appId,
                    MessageTTL = TimeSpan.FromSeconds(ttlSeconds)
                };
                return RabbitMqEventBus.CreateAsync(conn, logger, queueTypeMap, eventBusOptions).GetAwaiter().GetResult();
            }).As<IEventBus>().SingleInstance();

            builder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterType<RabbitMqListenerWorker>().AsSelf().SingleInstance().AutoActivate();
        }


        if (eventStoreProvider == "Redis")
        {
            builder.Register(ctx =>
            {
                var redisDb = ctx.Resolve<IDatabase>();
                var eventBus = ctx.Resolve<IEventBus>();
                var sagaIdGen = ctx.Resolve<ISagaIdGenerator>();
                var sagaCompensationCoordinator = ctx.Resolve<ISagaCompensationCoordinator>();

                var options = new SagaStoreOptions
                {
                    ApplicationId = appId,
                    StepLogTtl = TimeSpan.FromSeconds(ttlSeconds),
                    LogMaxRetryCount = logMaxRetryCount,
                };
                return new RedisSagaStore(redisDb, eventBus, sagaIdGen, sagaCompensationCoordinator, options);
            }).As<ISagaStore>().InstancePerLifetimeScope();
        }
    }
    public static void AddLycia(this ContainerBuilder builder, LyciaOptions options = null, Type sagaType = null)
    {
        var eventBusProvider = options?.EventBusProvider ?? "RabbitMQ";
        var eventStoreProvider = options?.EventStoreProvider ?? "Redis";
        var appId = options?.ApplicationId ?? throw new InvalidOperationException("ApplicationId is not configured.");
        var ttlSeconds = options?.CommonTtlSeconds > 0 ? options.CommonTtlSeconds : Constants.Ttl;
        var logMaxRetryCount = options?.LogMaxRetryCount > 0 ? options.LogMaxRetryCount : Constants.LogMaxRetryCount;

        builder.RegisterType<DefaultSagaIdGenerator>().As<ISagaIdGenerator>().InstancePerLifetimeScope();
        builder.RegisterType<SagaDispatcher>().As<ISagaDispatcher>().InstancePerLifetimeScope();
        builder.RegisterType<SagaCompensationCoordinator>().As<ISagaCompensationCoordinator>().InstancePerLifetimeScope();

        if (eventStoreProvider == "Redis")
        {
            var conn = options?.EventStoreConnectionString ?? throw new InvalidOperationException("Lycia:EventStore:ConnectionString is not configured.");
            builder.RegisterInstance(ConnectionMultiplexer.Connect(conn)).As<IConnectionMultiplexer>().SingleInstance();
            builder.Register(ctx => ctx.Resolve<IConnectionMultiplexer>().GetDatabase()).As<IDatabase>().InstancePerLifetimeScope();
        }

        var queueTypeMap = SagaHandlerRegistrationExtensions.DiscoverQueueTypeMap(appId, sagaType?.Assembly ?? Assembly.GetCallingAssembly());

        if (eventBusProvider == "RabbitMQ")
        {
            builder.Register(ctx =>
            {
                var conn = options?.EventBusConnectionString ?? throw new InvalidOperationException("Lycia:EventBus:ConnectionString is not configured.");
                // Logger implementation? (Autofac ile resolve edeceğin logger ekle!)
                var logger = ctx.ResolveOptional<ILogger<RabbitMqEventBus>>();
                var eventBusOptions = new EventBusOptions
                {
                    ApplicationId = appId,
                    MessageTTL = TimeSpan.FromSeconds(ttlSeconds)
                };
                return RabbitMqEventBus.CreateAsync(conn, logger, queueTypeMap, eventBusOptions).GetAwaiter().GetResult();
            }).As<IEventBus>().SingleInstance();

            builder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterType<RabbitMqListenerWorker>().AsSelf().SingleInstance().AutoActivate();
        }

        if (eventStoreProvider == "Redis")
        {
            builder.Register(ctx =>
            {
                var redisDb = ctx.Resolve<IDatabase>();
                var eventBus = ctx.Resolve<IEventBus>();
                var sagaIdGen = ctx.Resolve<ISagaIdGenerator>();
                var sagaCompensationCoordinator = ctx.Resolve<ISagaCompensationCoordinator>();

                var options = new SagaStoreOptions
                {
                    ApplicationId = appId,
                    StepLogTtl = TimeSpan.FromSeconds(ttlSeconds),
                    LogMaxRetryCount = logMaxRetryCount,
                };
                return new RedisSagaStore(redisDb, eventBus, sagaIdGen, sagaCompensationCoordinator, options);
            }).As<ISagaStore>().InstancePerLifetimeScope();
        }
    }

    public static void AddLyciaInMemory(this ContainerBuilder builder)
    {
        builder.RegisterType<DefaultSagaIdGenerator>().As<ISagaIdGenerator>().InstancePerLifetimeScope();
        builder.RegisterType<SagaDispatcher>().As<ISagaDispatcher>().InstancePerLifetimeScope();
        builder.RegisterType<SagaCompensationCoordinator>().As<ISagaCompensationCoordinator>().InstancePerLifetimeScope();
        builder.RegisterType<InMemorySagaStore>().As<ISagaStore>().InstancePerLifetimeScope();
        builder.RegisterType<InMemoryEventBus>().As<IEventBus>().InstancePerLifetimeScope();
    }
#else
    public static ILyciaServiceCollection AddLycia(this IServiceCollection services, IConfiguration? configuration = null, Type? sagaType = null)
    {
        if (configuration is null) return new LyciaServiceCollection(services, null);

        var eventBusProvider = configuration["Lycia:EventBus:Provider"] ?? "RabbitMQ";
        var eventStoreProvider = configuration["Lycia:EventStore:Provider"] ?? "Redis";
        // ApplicationId resolution: config["ApplicationId"]
        var appId = configuration["ApplicationId"] ??
                    throw new InvalidOperationException("ApplicationId is not configured.");
        // TTL resolution: use Lycia:CommonTTL
        var ttlSeconds = int.TryParse(configuration["Lycia:CommonTTL"], out var parsedTtl) && parsedTtl > 0
            ? parsedTtl
            : Constants.Ttl;

        var logMaxRetryCount = int.TryParse(configuration["Lycia:EventStore:LogMaxRetryCount"], out var parsedLogRetryMaxCount) && parsedLogRetryMaxCount > 0
            ? parsedLogRetryMaxCount
            : Constants.LogMaxRetryCount;

        // Production default registration for ISagaIdGenerator
        services.TryAddScoped<ISagaIdGenerator, DefaultSagaIdGenerator>();
        // Production default registration for ISagaDispatcher
        services.TryAddScoped<ISagaDispatcher, SagaDispatcher>();
        // Production default registration for ISagaCompensationCoordinator
        services.TryAddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();

        // Add Redis connection (if Provider is Redis)
        if (eventStoreProvider == "Redis")
        {
            var conn = configuration["Lycia:EventStore:ConnectionString"]
                       ?? throw new InvalidOperationException("Lycia:EventStore:ConnectionString is not configured.");
            services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(conn));
            services.AddScoped<IDatabase>(provider => provider.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
        }

        var queueTypeMap =
            SagaHandlerRegistrationExtensions.DiscoverQueueTypeMap(appId, sagaType?.Assembly ?? Assembly.GetCallingAssembly());

        // Configure and add RabbitMqEventBus (if Provider is RabbitMQ)
        if (eventBusProvider == "RabbitMQ")
        {
            services.AddSingleton<IEventBus>(provider =>
            {

                var conn = configuration["Lycia:EventBus:ConnectionString"] ?? throw new InvalidOperationException("Lycia:EventBus:ConnectionString is not configured.");

                var logger = provider.GetRequiredService<ILogger<RabbitMqEventBus>>();

                var eventBusOptions = new EventBusOptions
                {
                    ApplicationId = appId,
                    MessageTTL = TimeSpan.FromSeconds(ttlSeconds)
                };
                return RabbitMqEventBus.CreateAsync(conn, logger, queueTypeMap, eventBusOptions).GetAwaiter().GetResult();
            });

            services.AddHostedService<RabbitMqListener>();

        }

        // Configure and add RedisSagaStore
        if (eventStoreProvider == "Redis")
        {
            services.AddScoped<ISagaStore, RedisSagaStore>(provider =>
            {
                var redisDb = provider.GetRequiredService<IDatabase>();
                var eventBus = provider.GetRequiredService<IEventBus>();
                var sagaIdGen = provider.GetRequiredService<ISagaIdGenerator>();
                var sagaCompensationCoordinator = provider.GetRequiredService<ISagaCompensationCoordinator>();

                var options = new SagaStoreOptions
                {
                    ApplicationId = appId,
                    StepLogTtl = TimeSpan.FromSeconds(ttlSeconds),
                    LogMaxRetryCount = logMaxRetryCount,
                };
                return new RedisSagaStore(redisDb, eventBus, sagaIdGen, sagaCompensationCoordinator, options);
            });
        }

        return new LyciaServiceCollection(services, configuration);
    }

    public static ILyciaServiceCollection AddLyciaInMemory(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.TryAddScoped<ISagaIdGenerator, DefaultSagaIdGenerator>();
        services.TryAddScoped<ISagaDispatcher, SagaDispatcher>();
        services.TryAddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.TryAddScoped<ISagaStore, InMemorySagaStore>();
        services.TryAddScoped<IEventBus, InMemoryEventBus>();
        return new LyciaServiceCollection(services, configuration);
    }
#endif
}