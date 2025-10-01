// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using System.Reflection;
using System.Runtime.CompilerServices;
using Lycia.Helpers;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Listener;
using Lycia.Extensions.Serialization;
using Lycia.Extensions.Stores;
using Lycia.Common;
using Lycia.Common.Configurations;
using Lycia.Compensating;
using Lycia.Dispatching;
using Lycia.Eventing;
using Lycia.Middleware;
using Lycia.Retry;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Middlewares;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Messaging.Handlers;
using Lycia.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Retry;
using StackExchange.Redis;
using IRetryPolicy = Lycia.Retry.IRetryPolicy;

[assembly: InternalsVisibleTo("Lycia.Extensions.Scheduling.Redis")]
namespace Lycia.Extensions;

/// <summary>
/// Registration entrypoints + fluent builder for Lycia.
/// Consumers call services.AddLycia(configuration)
///   .UseMessageSerializer<...>()
///   .UseEventBus<...>()
///   .UseSagaStore<...>()
///   .AddSagasFromAssemblies(typeof(SomeHandler).Assembly)
///   .Build();
/// </summary>
public static class LyciaRegistrationExtensions
{
    /// <summary>
    /// Binds options from configuration, registers sensible defaults, and returns a fluent builder
    /// so callers can customize transports, store, and serializer, and register handlers.
    /// </summary>
    public static LyciaBuilder AddLycia(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();
        var rootAppId = configuration["ApplicationId"];
        var commonTtlSeconds = configuration.GetValue<int?>("Lycia:CommonTTL");

        // --- Redis connection (only if EventStore provider is Redis) ---
        var storeSection = configuration.GetSection(SagaStoreOptions.SectionName);

        // 1) Bind options with defaults
        services
            .AddOptions<EventBusOptions>()
            .Bind(configuration.GetSection(EventBusOptions.SectionName))
            .PostConfigure(o =>
            {
                o.Provider ??= Constants.ProviderRabbitMq;
                // Root ApplicationId fall-back
                o.ApplicationId ??= rootAppId;

                // CommonTTL -> MessageTTL / DLQ TTL 
                if (commonTtlSeconds is > 0)
                {
                    var tt = TimeSpan.FromSeconds(commonTtlSeconds.Value);
                    o.MessageTTL ??= tt;
                    o.DeadLetterQueueMessageTTL ??= tt;
                }
            });

        ConfigureRedisEventStore(services, storeSection);
        services
            .AddOptions<SagaStoreOptions>()
            .Bind(configuration.GetSection(SagaStoreOptions.SectionName))
            .PostConfigure(o =>
            {
                o.Provider ??= Constants.ProviderRedis;
                if (o.LogMaxRetryCount <= 0) o.LogMaxRetryCount = Constants.LogMaxRetryCount;
                o.ApplicationId ??= rootAppId;

                if (commonTtlSeconds is > 0)
                {
                    var tt = TimeSpan.FromSeconds(commonTtlSeconds.Value);
                    o.StepLogTtl ??= tt;
                }
            });

        services
            .AddOptions<SagaOptions>()
            .Bind(configuration.GetSection("Lycia:Saga"))
            .PostConfigure(o => { o.DefaultIdempotency ??= true; });

        services
            .AddOptions<LoggingOptions>()
            .Bind(configuration.GetSection("Lycia:Logging"))
            .PostConfigure(o =>
            {
                if (o.PayloadMaxLength <= 0) o.PayloadMaxLength = 2048;
            });

        // 2) Common/core services (can be overridden later via builder)
        services.TryAddScoped<ISagaIdGenerator, DefaultSagaIdGenerator>();
        services.TryAddScoped<ISagaDispatcher, SagaDispatcher>();
        services.TryAddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();
        services.TryAddScoped<ISagaContextAccessor, SagaContextAccessor>();

        // Serializer default
        services.TryAddSingleton<IMessageSerializer, NewtonsoftJsonMessageSerializer>();

        // Placeholder for queue map; the builder will populate it in Build()
        services.TryAddSingleton<IDictionary<string, (Type MessageType, Type HandlerType)>>(sp =>
            new Dictionary<string, (Type, Type)>());

        // Default EventBus (RabbitMQ). Consumers may override with UseEventBus<T>().
        services.TryAddSingleton<IEventBus>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RabbitMqEventBus>>();
            var ebOptions = sp.GetRequiredService<IOptions<EventBusOptions>>().Value;
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var map = sp.GetRequiredService<IDictionary<string, (Type MessageType, Type HandlerType)>>();

            if (!string.Equals(ebOptions.Provider, Constants.ProviderRabbitMq, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported EventBus provider '{ebOptions.Provider}'. " +
                                                    "Override with LyciaBuilder.UseEventBus<T>() to supply a custom bus.");
            }

            if (string.IsNullOrWhiteSpace(ebOptions.ConnectionString))
                throw new InvalidOperationException("Lycia:EventBus:ConnectionString is required.");

            return RabbitMqEventBus.CreateAsync(
                logger: logger,
                queueTypeMap: map,
                options: ebOptions,
                serializer: serializer
            ).GetAwaiter().GetResult();
        });

        // Default SagaStore (Redis). Consumers may override with UseSagaStore<T>().
        services.TryAddScoped<ISagaStore>(sp =>
        {
            var storeOpts = sp.GetRequiredService<IOptions<SagaStoreOptions>>().Value;
            var eventBus = sp.GetRequiredService<IEventBus>();
            var idGen = sp.GetRequiredService<ISagaIdGenerator>();
            var compCoord = sp.GetRequiredService<ISagaCompensationCoordinator>();

            if (!string.Equals(storeOpts.Provider, Constants.ProviderRedis, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported SagaStore provider '{storeOpts.Provider}'. " +
                                                    "Override with LyciaBuilder.UseSagaStore<T>() to supply a custom store.");
            }

            // Expect IDatabase to be registered by the host app (same as before)
            var redis = sp.GetRequiredService<IDatabase>();
            return new RedisSagaStore(redis, eventBus, idGen, compCoord, storeOpts);
        });

        RegisterMiddlewareAndPolicies(services);

        services.AddHostedService<RabbitMqListener>();

        // Health checks
        services.AddHealthChecks().AddCheck<Helpers.LyciaHealthCheck>("Lycia");

        return new LyciaBuilder(services, configuration);
    }

    /// <summary>
    /// Adds Lycia and allows inline configuration of LyciaBuilder. 
    /// All Configure* methods are only valid inside this action.
    /// </summary>
    public static LyciaBuilder AddLycia(
        this IServiceCollection services,
        Action<LyciaBuilder> configure,
        IConfiguration configuration)
    {
        var builder = AddLycia(services, configuration);
        builder.SetInlineConfigure(true);
        try
        {
            configure.Invoke(builder);
        }
        finally
        {
            builder.SetInlineConfigure(false);
        }

        return builder;
    }

    private static void RegisterMiddlewareAndPolicies(IServiceCollection services)
    {
        // Default retry policy (Polly-based). Can be overridden by registering IRetryPolicy before Build().
        services.TryAddSingleton<IRetryPolicy, PollyRetryPolicy>();

        // 1) Ensure default middlewares are available as ISagaMiddleware (idempotent, allow multiple implementations)
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(ISagaMiddleware), typeof(LoggingMiddleware)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(ISagaMiddleware), typeof(RetryMiddleware)));

        // 2) Default ordered pipeline: Logging first, then Retry
        //    This will only be used if the host app does NOT call UseSagaMiddleware(Action<>)
        //    because that overload registers its own IReadOnlyList<Type>.
        services.TryAddScoped<IReadOnlyList<Type>>(_ => new List<Type>
        {
            typeof(LoggingMiddleware),
            typeof(RetryMiddleware)
        });
    }

    private static void ConfigureRedisEventStore(IServiceCollection services, IConfigurationSection storeSection)
    {
        var storeProvider = storeSection["Provider"] ?? Constants.ProviderRedis;
        if (storeProvider.Equals(Constants.ProviderRedis, StringComparison.OrdinalIgnoreCase))
        {
            var redisConn = storeSection["ConnectionString"];
            if (string.IsNullOrWhiteSpace(redisConn))
                throw new InvalidOperationException(
                    "Lycia:EventStore:ConnectionString is required for Redis provider.");

            services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn!));

            services.TryAddScoped<IDatabase>(sp =>
                sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
        }
    }

    /// <summary>
    /// Lightweight in-memory setup for tests or samples. Registers in-memory EventBus and SagaStore,
    /// keeps the same fluent builder so you can still add handlers and override pieces.
    /// </summary>
    public static LyciaBuilder AddLyciaInMemory(this IServiceCollection services, IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder().AddInMemoryCollection().Build();
        services.AddLogging();

        // Bind minimal options with sensible defaults
        services
            .AddOptions<EventBusOptions>()
            .Configure(o =>
            {
                o.Provider = "InMemory";
                o.ApplicationId ??= configuration["ApplicationId"]; // optional
            });

        services
            .AddOptions<SagaStoreOptions>()
            .Configure(o =>
            {
                o.Provider = "InMemory";
                o.ApplicationId ??= configuration["ApplicationId"]; // optional
                if (o.LogMaxRetryCount <= 0) o.LogMaxRetryCount = Constants.LogMaxRetryCount;
            });

        services
            .AddOptions<SagaOptions>()
            .Configure(o => { o.DefaultIdempotency ??= true; });

        // Core services
        services.TryAddScoped<ISagaIdGenerator, DefaultSagaIdGenerator>();
        services.TryAddScoped<ISagaContextAccessor, SagaContextAccessor>();
        services.TryAddScoped<ISagaDispatcher, SagaDispatcher>();
        services.TryAddScoped<ISagaCompensationCoordinator, SagaCompensationCoordinator>();

        // Serializer
        services.RemoveAll(typeof(IMessageSerializer));
        services.AddSingleton<IMessageSerializer, NewtonsoftJsonMessageSerializer>();

        // In-memory transports/stores
        services.RemoveAll(typeof(IEventBus));
        services.AddScoped<IEventBus>(sp =>
            new InMemoryEventBus(new Lazy<ISagaDispatcher>(sp.GetRequiredService<ISagaDispatcher>)));

        services.RemoveAll(typeof(ISagaStore));
        services.AddScoped<ISagaStore, InMemorySagaStore>();

        // Placeholder queue map – builder will populate in Build()
        services.TryAddSingleton<IDictionary<string, (Type MessageType, Type HandlerType)>>(sp =>
            new Dictionary<string, (Type, Type)>(StringComparer.OrdinalIgnoreCase));

        RegisterMiddlewareAndPolicies(services);
        // Health checks for in-memory setup as well (trivial pass)
        services.AddHealthChecks().AddCheck<Helpers.LyciaHealthCheck>("Lycia");
        return new LyciaBuilder(services, configuration);
    }
}

/// <summary>
/// Fluent builder that lets the app override serializer/bus/store and register saga handlers.
/// </summary>
public sealed class LyciaBuilder
{
    private bool _inlineConfigureGate;
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;
    private readonly List<Assembly> _assemblies = new();
    private readonly HashSet<Type> _explicitHandlerTypes = new();

    private readonly Dictionary<string, (Type MessageType, Type HandlerType)> _manualMap =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _appIdCache;

    internal LyciaBuilder(IServiceCollection services, IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
        _appIdCache = _configuration["ApplicationId"] ??
                      throw new InvalidOperationException("ApplicationId is not configured.");
    }

    // --- bridge for external extensions (kept internal) ---
    internal IServiceCollection Services => _services;
    internal IConfiguration ConfigurationRoot => _configuration;
    internal void ThrowIfNotInline(string method) => EnsureInlineConfigure(method);

    internal void SetInlineConfigure(bool enabled) => _inlineConfigureGate = enabled;

    private void EnsureInlineConfigure(string method)
    {
        if (!_inlineConfigureGate)
            throw new InvalidOperationException(
                $"{method} can only be called inside AddLycia(o => {{ ... }}, configuration) block.");
    }

    // ---------------------------
    // Overrides
    // ---------------------------
    /// <summary>
    /// Configures the message serializer for the system by removing any existing implementation
    /// and registering a new one of the specified type.
    /// </summary>
    /// <typeparam name="TSerializer">The type of the message serializer to register. Must implement <see cref="IMessageSerializer"/>.</typeparam>
    /// <returns>The current instance of <see cref="LyciaBuilder"/> for further configuration.</returns>
    public LyciaBuilder UseMessageSerializer<TSerializer>() where TSerializer : class, IMessageSerializer
    {
        _services.RemoveAll(typeof(IMessageSerializer));
        _services.AddSingleton<IMessageSerializer, TSerializer>();
        return this;
    }

    /// <summary>
    /// Configures the application to use a specific implementation of the IEventBus interface.
    /// This method removes any previously registered event bus and registers the provided type
    /// as the singleton implementation of the event bus.
    /// </summary>
    /// <typeparam name="TEventBus">The type of the event bus to use, which must implement the IEventBus interface.</typeparam>
    /// <returns>A LyciaBuilder instance to allow further configuration chaining.</returns>
    public LyciaBuilder UseEventBus<TEventBus>() where TEventBus : class, IEventBus
    {
        _services.RemoveAll(typeof(IEventBus));
        _services.AddSingleton<IEventBus, TEventBus>();
        return this;
    }

    /// <summary>
    /// Configures the saga store by registering a custom implementation of the specified type
    /// and ensures it is used throughout the saga lifecycle for persistence and step tracking.
    /// </summary>
    /// <typeparam name="TSagaStore">The type of the saga store implementation to be used.</typeparam>
    /// <returns>A fluent builder that allows further customization of saga configuration.</returns>
    public LyciaBuilder UseSagaStore<TSagaStore>() where TSagaStore : class, ISagaStore
    {
        _services.RemoveAll(typeof(ISagaStore));
        _services.AddSingleton<ISagaStore, TSagaStore>();
        return this;
    }

    /// <summary>
    /// Replaces the logging middleware slot with a custom implementation.
    /// </summary>
    public LyciaBuilder UseLoggingMiddleware<TLogging>()
        where TLogging : class, ISagaMiddleware, ILoggingSagaMiddleware
    {
        // Remove default LoggingMiddleware registration (if present)
        for (var i = _services.Count - 1; i >= 0; i--)
        {
            var sd = _services[i];
            if (sd.ServiceType == typeof(ISagaMiddleware) && sd.ImplementationType == typeof(LoggingMiddleware))
                _services.RemoveAt(i);
        }

        // Ensure custom logging middleware is registered once
        var exists = _services.Any(sd => sd.ServiceType == typeof(ISagaMiddleware) &&
                                         sd.ImplementationType == typeof(TLogging));
        if (!exists) _services.AddScoped(typeof(ISagaMiddleware), typeof(TLogging));

        // Default ordered pipeline: Logging first, then Retry
        _services.RemoveAll(typeof(IReadOnlyList<Type>));
        _services.AddScoped<IReadOnlyList<Type>>(_ => new List<Type>
        {
            typeof(TLogging),
            typeof(RetryMiddleware)
        });

        return this;
    }

    /// <summary>
    /// Configures and registers saga middleware in a pipeline for handling saga behaviors such as logging and retry policies.
    /// </summary>
    /// <param name="configure">An optional configuration action to customize the saga middleware pipeline, such as adding specific middleware components.</param>
    /// <returns>An updated instance of <see cref="LyciaBuilder"/>, allowing further configuration.</returns>
    /// <exception cref="InvalidOperationException">Thrown if an invalid middleware type is specified in the configuration.</exception>
    public LyciaBuilder UseSagaMiddleware(Action<SagaMiddlewareOptions>? configure)
    {
        if (configure == null) return this;
        var options = new SagaMiddlewareOptions();
        configure(options);

        // Slot-based plan: fixed positions for Lycia-known categories, replacements by interface
        //  - ILoggingSagaMiddleware slot (default: LoggingMiddleware)
        //  - IRetrySagaMiddleware   slot (default: RetryMiddleware)
        var slotMap = new Dictionary<Type, Type>
        {
            { typeof(ILoggingSagaMiddleware), typeof(LoggingMiddleware) },
            { typeof(IRetrySagaMiddleware), typeof(RetryMiddleware) }
        };

        // Extra middlewares which only implement ISagaMiddleware (no known slot)
        var extras = new List<Type>();

        foreach (var t in options.Middlewares)
        {
            if (t == null) continue;

            if (typeof(ILoggingSagaMiddleware).IsAssignableFrom(t))
            {
                // Replace logging slot
                slotMap[typeof(ILoggingSagaMiddleware)] = t;
                RemoveMiddlewareImplementation(typeof(LoggingMiddleware));
            }
            else if (typeof(IRetrySagaMiddleware).IsAssignableFrom(t))
            {
                // Replace retry slot
                slotMap[typeof(IRetrySagaMiddleware)] = t;
                RemoveMiddlewareImplementation(typeof(RetryMiddleware));
            }
            else if (typeof(ISagaMiddleware).IsAssignableFrom(t))
            {
                // No known slot → append later in the given order
                if (!extras.Contains(t)) extras.Add(t);
            }
            else
            {
                throw new InvalidOperationException($"Type {t.FullName} does not implement ISagaMiddleware.");
            }
        }

        // Build the final ordered list: Logging → Retry → extras (in user order)
        var ordered = new List<Type>
        {
            slotMap[typeof(ILoggingSagaMiddleware)],
            slotMap[typeof(IRetrySagaMiddleware)]
        };

        // avoid duplicates when extras accidentally include defaults
        foreach (var t in extras.Where(t => !ordered.Contains(t)))
        {
            ordered.Add(t);
        }

        // Register each middleware as ISagaMiddleware scoped in DI, avoiding duplicates
        foreach (var t in from t in ordered
                 let exists =
                     _services.Any(sd => sd.ServiceType == typeof(ISagaMiddleware) && sd.ImplementationType == t)
                 where !exists
                 select t)
        {
            _services.AddScoped(typeof(ISagaMiddleware), t);
        }

        // Register ordered list for pipeline resolution
        _services.RemoveAll(typeof(IReadOnlyList<Type>));
        _services.AddScoped<IReadOnlyList<Type>>(_ => ordered);
        return this;

        // Helper to remove a specific ISagaMiddleware implementation mapping
        void RemoveMiddlewareImplementation(Type impl)
        {
            for (var i = _services.Count - 1; i >= 0; i--)
            {
                var sd = _services[i];
                if (sd.ServiceType == typeof(ISagaMiddleware) && sd.ImplementationType == impl)
                    _services.RemoveAt(i);
            }
        }
    }

    // ---------------------------
    // Handler discovery (fluent)
    // ---------------------------
    /// <summary>
    /// Registers handlers from the specified assemblies and adds them to the builder.
    /// Ensures that handlers in the assemblies are discovered and included in the configuration.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for handler types.</param>
    /// <returns>A configured instance of the <see cref="LyciaBuilder"/> to allow for fluent chaining of additional configuration methods.</returns>
    public LyciaBuilder AddHandlersFrom(params Assembly[]? assemblies)
    {
        if (assemblies == null) return this;

        foreach (var a in assemblies.Where(x => x != null))
            _assemblies.Add(a);

        return this;
    }

    /// <summary>
    /// Registers a specific saga handler type for dependency injection, enabling it to handle associated messages.
    /// Also updates routing and message-type discovery for the provided saga handler.
    /// </summary>
    /// <param name="handlerType">The type of the saga handler to register. Must implement message handling interfaces.</param>
    /// <returns>An instance of <see cref="LyciaBuilder"/> to allow further configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handlerType"/> is null.</exception>
    public LyciaBuilder AddSaga(Type handlerType)
    {
        if (handlerType == null) throw new ArgumentNullException(nameof(handlerType));

        // register handler concrete type for DI
        _services.AddScoped(handlerType);
        _explicitHandlerTypes.Add(handlerType);
        AddHandlersFrom(handlerType.Assembly);

        foreach (var messageType in _LyciaHandlerDiscovery.GetMessageTypesFromHandler(handlerType))
        {
            var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, _appIdCache);
            _manualMap[routingKey] = (messageType, handlerType);
        }

        return this;
    }

    /// <summary>
    /// Adds multiple saga handler types to the builder for registration.
    /// </summary>
    /// <param name="handlerTypes">An array of types representing saga handlers to be registered.</param>
    /// <returns>The current instance of <see cref="LyciaBuilder"/> to allow for method chaining.</returns>
    public LyciaBuilder AddSagas(params Type?[]? handlerTypes)
    {
        if (handlerTypes == null) return this;
        foreach (var t in handlerTypes)
        {
            if (t == null) continue;
            AddSaga(t);
        }

        return this;
    }

    /// <summary>
    /// Automatically scans and registers all sagas from the assembly of the calling code.
    /// This method simplifies the process of locating and registering saga handlers
    /// by using the assembly where the method is invoked as the reference.
    /// </summary>
    /// <returns>
    /// A fluent <see cref="LyciaBuilder"/> instance to allow further configuration.
    /// </returns>
    public LyciaBuilder AddSagasFromCurrentAssembly()
    {
        var calling = Assembly.GetCallingAssembly();
        return AddSagasFromAssemblies(calling);
    }

    /// <summary>
    /// Registers saga handlers by discovering them from the assemblies containing the specified marker types.
    /// </summary>
    /// <param name="markerTypes">The types used to locate the assemblies to be scanned for saga handlers.</param>
    /// <returns>A fluent builder allowing further customization of the Lycia configuration.</returns>
    public LyciaBuilder AddSagasFromAssembliesOf(params Type[] markerTypes)
    {
        var asms = markerTypes?.Where(t => t != null).Select(t => t.Assembly).Distinct().ToArray() ?? [];
        return AddSagasFromAssemblies(asms);
    }

    /// <summary>
    /// Discovers and registers saga handlers from the specified assemblies to the service collection.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for saga handlers.</param>
    /// <returns>A fluent builder for additional configuration.</returns>
    public LyciaBuilder AddSagasFromAssemblies(params Assembly[] assemblies)
    {
        AddHandlersFrom(assemblies);
        foreach (var asm in assemblies)
        {
            foreach (var handlerType in _LyciaHandlerDiscovery.SafeGetTypes(asm)
                         .Where(t => t is { IsAbstract: false, IsInterface: false } &&
                                     (_LyciaHandlerDiscovery.IsSagaHandlerBase(t) ||
                                      _LyciaHandlerDiscovery.ImplementsAnySagaInterface(t))))
            {
                _services.AddScoped(handlerType);
                foreach (var messageType in _LyciaHandlerDiscovery.GetMessageTypesFromHandler(handlerType))
                {
                    var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, _appIdCache);
                    _manualMap[routingKey] = (messageType, handlerType);
                }
            }
        }

        return this;
    }

    // ---------------------------
    // Configure options via code (stacked on top of appsettings)
    // ---------------------------
    /// <summary>
    /// Configures saga options by allowing customization of the provided <see cref="SagaOptions"/> via a callback.
    /// Applies additional options on top of existing app settings.
    /// </summary>
    /// <param name="configure">A callback action to configure the <see cref="SagaOptions"/>.</param>
    /// <returns>Returns the current <see cref="LyciaBuilder"/> instance for chaining further configuration.</returns>
    public LyciaBuilder ConfigureSaga(Action<SagaOptions>? configure)
    {
        EnsureInlineConfigure(nameof(ConfigureSaga));
        if (configure != null) _services.PostConfigure(configure);
        return this;
    }

    /// <summary>
    /// Configures the Event Bus by applying the specified configuration settings.
    /// </summary>
    /// <param name="configure">An action to configure the <see cref="EventBusOptions"/>, allowing customization of properties such as provider, connection string, and message settings.</param>
    /// <returns>A <see cref="LyciaBuilder"/> instance to continue configuring the application with a fluent interface.</returns>
    public LyciaBuilder ConfigureEventBus(Action<EventBusOptions>? configure)
    {
        EnsureInlineConfigure(nameof(ConfigureSaga));
        if (configure != null) _services.PostConfigure(configure);
        return this;
    }

    /// <summary>
    /// Configures the saga store by applying the specified options, which allows customization
    /// of store-specific settings such as connection strings, retry policies, and other configurations.
    /// </summary>
    /// <param name="configure">A delegate to configure <see cref="SagaStoreOptions"/> with specific values.</param>
    /// <returns>The <see cref="LyciaBuilder"/> instance to enable further configuration chaining.</returns>
    public LyciaBuilder ConfigureSagaStore(Action<SagaStoreOptions>? configure)
    {
        EnsureInlineConfigure(nameof(ConfigureSaga));
        if (configure != null) _services.PostConfigure(configure);
        return this;
    }

    /// <summary>
    /// Configures retry strategy options by allowing customization of the provided <see cref="RetryStrategyOptions"/> via a callback.
    /// Applies additional options on top of existing app settings.
    /// </summary>
    /// <param name="configure">A callback action to configure the <see cref="RetryStrategyOptions"/>.</param>
    /// <returns>Returns the current <see cref="LyciaBuilder"/> instance for chaining further configuration.</returns>
    public LyciaBuilder ConfigureRetry(Action<RetryStrategyOptions>? configure)
    {
        EnsureInlineConfigure(nameof(ConfigureSaga));
        if (configure != null) _services.PostConfigure(configure);
        return this;
    }

    // appsettings: "Lycia:Retry"
    /// <summary>
    /// Configures retry strategy options by binding them to the provided configuration section ("Lycia:Retry").
    /// Allows additional customization of retry behavior through defined options.
    /// </summary>
    /// <returns>Returns the current <see cref="LyciaBuilder"/> instance for chaining further configuration.</returns>
    public LyciaBuilder ConfigureRetry()
    {
        EnsureInlineConfigure(nameof(ConfigureSaga));
        _services.AddOptions<RetryStrategyOptions>()
            .Bind(_configuration.GetSection("Lycia:Retry"));
        return this;
    }

    /// <summary>
    /// Configures logging behavior used by the built-in LoggingMiddleware.
    /// Does not change the middleware type; only adjusts its options.
    /// </summary>
    public LyciaBuilder ConfigureLogging(Action<LoggingOptions>? configure)
    {
        EnsureInlineConfigure(nameof(ConfigureLogging));
        if (configure != null) _services.PostConfigure(configure);
        return this;
    }

    // ---------------------------
    // Finalize
    // ---------------------------
    /// <summary>
    /// Finalizes the builder by discovering and registering message handlers, sagas,
    /// and queue type mappings into the service collection.
    /// Ensures all explicitly added handlers are registered and merges with discovered handlers.
    /// </summary>
    /// <returns>The current instance of <see cref="LyciaBuilder"/> for continued modifications or finalization.</returns>
    public LyciaBuilder Build()
    {
        if (_assemblies.Count == 0)
        {
            var entry = Assembly.GetEntryAssembly();
            if (entry != null) _assemblies.Add(entry);
        }

        var discovered = _LyciaHandlerDiscovery.DiscoverQueueTypeMap(_appIdCache, _assemblies.ToArray());

        // ensure explicit handler types are available for DI (already added in AddSaga/AddSagas..., but keep safe)
        foreach (var ht in _explicitHandlerTypes)
            _services.TryAddScoped(ht);

        // merge: explicit/manual overrides discovery
        foreach (var kv in _manualMap)
            discovered[kv.Key] = kv.Value;

        _services.AddSingleton<IDictionary<string, (Type MessageType, Type HandlerType)>>(sp =>
        {
            var dict = new Dictionary<string, (Type, Type)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in discovered) dict[kv.Key] = kv.Value;
            return dict;
        });

        return this;
    }
}

// Internal helper copied from previous SagaHandlerRegistrationExtensions
internal static class _LyciaHandlerDiscovery
{
    internal static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    internal static bool IsSagaHandlerBase(Type? type)
    {
        return GetGenericBaseType(type, typeof(StartReactiveSagaHandler<>)) != null
               || GetGenericBaseType(type, typeof(ReactiveSagaHandler<>)) != null
               || GetGenericBaseType(type, typeof(StartCoordinatedSagaHandler<,>)) != null
               || GetGenericBaseType(type, typeof(StartCoordinatedResponsiveSagaHandler<,,>)) != null
               || GetGenericBaseType(type, typeof(CoordinatedResponsiveSagaHandler<,,>)) != null
               || GetGenericBaseType(type, typeof(CoordinatedSagaHandler<,>)) != null;
    }

    internal static bool ImplementsAnySagaInterface(Type? type)
    {
        if (type == null) return false;
        return type.GetInterfaces().Any(i =>
            i.IsGenericType && (
                i.GetGenericTypeDefinition() == typeof(ISagaHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>) ||
                i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>) ||
                i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(IResponseSagaHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)
            ));
    }

    private static Type? GetGenericBaseType(Type? type, Type genericBaseType)
    {
        while (type != null && type != typeof(object))
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericBaseType)
                return type;
            type = type.BaseType;
        }

        return null;
    }

    internal static IEnumerable<Type> GetMessageTypesFromHandler(Type handlerType)
    {
        var messageTypes = GetMessageTypes(handlerType);

        // From known generic base classes
        Type? current = handlerType;
        while (current != null && current != typeof(object))
        {
            if (current.IsGenericType)
            {
                var def = current.GetGenericTypeDefinition();
                var args = current.GetGenericArguments();

                if (def == typeof(StartReactiveSagaHandler<>) || def == typeof(ReactiveSagaHandler<>))
                    TryAdd(args[0], messageTypes);
                else if (def == typeof(StartCoordinatedSagaHandler<,>) || def == typeof(CoordinatedSagaHandler<,>))
                    TryAdd(args[0], messageTypes);
                else if (def == typeof(StartCoordinatedResponsiveSagaHandler<,,>) ||
                         def == typeof(CoordinatedResponsiveSagaHandler<,,>))
                {
                    TryAdd(args[0], messageTypes); // TMessage
                    TryAdd(args[1], messageTypes); // TResponse
                }
            }

            current = current.BaseType;
        }

        return messageTypes;

        static void TryAdd(Type candidate, HashSet<Type> set)
        {
            if (typeof(IMessage).IsAssignableFrom(candidate) && !typeof(SagaData).IsAssignableFrom(candidate))
                set.Add(candidate);
        }
    }

    private static HashSet<Type> GetMessageTypes(Type handlerType)
    {
        var set = new HashSet<Type>();
        foreach (var arg in GetInterfaceArgs(handlerType))
            set.Add(arg);
        return set;
    }

    private static IEnumerable<Type> GetInterfaceArgs(Type handlerType)
    {
        return handlerType.GetInterfaces()
            .Where(i => i.IsGenericType && (
                i.GetGenericTypeDefinition() == typeof(ISagaHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>) ||
                i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>) ||
                i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(IResponseSagaHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)
            ))
            .SelectMany(i => i.GetGenericArguments());
    }

    internal static Dictionary<string, (Type MessageType, Type HandlerType)> DiscoverQueueTypeMap(
        string? applicationId,
        params Assembly[] assemblies)
    {
        var pairs = assemblies
            .SelectMany(SafeGetTypes)
            .Where(t => t is { IsAbstract: false, IsInterface: false } && ImplementsAnySagaInterface(t))
            .SelectMany(handlerType =>
                GetMessageTypesFromHandler(handlerType)
                    .Select(messageType => new
                    {
                        Key = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, applicationId),
                        Value = (MessageType: messageType, HandlerType: handlerType)
                    }));

        var map = new Dictionary<string, (Type MessageType, Type HandlerType)>(StringComparer.OrdinalIgnoreCase);
        // Preserve current semantics: last one wins on duplicate keys
        foreach (var p in pairs)
            map[p.Key] = p.Value;

        return map;
    }
}