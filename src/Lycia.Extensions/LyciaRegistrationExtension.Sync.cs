// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
#if NETSTANDARD2_0
using Autofac;
using Lycia.Common;
using Lycia.Common.Configurations;
using Lycia.Compensating;
using Lycia.Dispatching;
using Lycia.Eventing;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Listener;
using Lycia.Extensions.Serialization;
using Lycia.Extensions.Stores;
using Lycia.Helpers;
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
using System.Reflection;
using IRetryPolicy = Lycia.Retry.IRetryPolicy;

namespace Lycia.Extensions
{
    /// <summary>
    /// Registration entrypoints + fluent builder for Lycia.
    /// Consumers call containerBuilder.AddLycia(configuration)
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
        public static LyciaBuilder AddLycia(this ContainerBuilder containerBuilder, IConfiguration configuration)
        {
            var rootAppId = configuration["ApplicationId"];
            var commonTtlSeconds = configuration.GetValue<int?>("Lycia:CommonTTL");

            // --- Redis connection (only if EventStore provider is Redis) ---
            var storeSection = configuration.GetSection(SagaStoreOptions.SectionName);

            // 1) Bind options with defaults
            var eb = new EventBusOptions();
            configuration.GetSection(EventBusOptions.SectionName).Bind(eb);
            eb.Provider ??= Constants.ProviderRabbitMq;
            eb.ApplicationId ??= rootAppId;
            if (commonTtlSeconds is > 0)
            {
                var tt = TimeSpan.FromSeconds(commonTtlSeconds.Value);
                eb.MessageTTL ??= tt;
                eb.DeadLetterQueueMessageTTL ??= tt;
            }
            containerBuilder.RegisterInstance(Options.Create(eb)).As<IOptions<EventBusOptions>>().SingleInstance();

            ConfigureRedisEventStore(containerBuilder, storeSection);

            var ss = new SagaStoreOptions();
            configuration.GetSection(SagaStoreOptions.SectionName).Bind(ss);
            ss.Provider ??= Constants.ProviderRedis;
            if (ss.LogMaxRetryCount <= 0) ss.LogMaxRetryCount = Constants.LogMaxRetryCount;
            ss.ApplicationId ??= rootAppId;
            if (commonTtlSeconds is > 0)
            {
                var tt = TimeSpan.FromSeconds(commonTtlSeconds.Value);
                ss.StepLogTtl ??= tt;
            }
            containerBuilder.RegisterInstance(Options.Create(ss)).As<IOptions<SagaStoreOptions>>().SingleInstance();

            var so = new SagaOptions();
            configuration.GetSection("Lycia:Saga").Bind(so);
            so.DefaultIdempotency ??= true;
            containerBuilder.RegisterInstance(Options.Create(so)).As<IOptions<SagaOptions>>().SingleInstance();

            var lo = new LoggingOptions();
            configuration.GetSection("Lycia:Logging").Bind(lo);
            if (lo.PayloadMaxLength <= 0) lo.PayloadMaxLength = 2048;
            containerBuilder.RegisterInstance(Options.Create(lo)).As<IOptions<LoggingOptions>>().SingleInstance();

            // 2) Common/core services (can be overridden later via builder)
            containerBuilder.RegisterType<DefaultSagaIdGenerator>().As<ISagaIdGenerator>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaDispatcher>().As<ISagaDispatcher>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaCompensationCoordinator>().As<ISagaCompensationCoordinator>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaContextAccessor>().As<ISagaContextAccessor>().InstancePerLifetimeScope();

            // Serializer default
            containerBuilder.RegisterType<NewtonsoftJsonMessageSerializer>().SingleInstance();
            containerBuilder.RegisterType<AvroMessageSerializer>().SingleInstance();
            containerBuilder.RegisterType<CompositeMessageSerializer>()
                .As<IMessageSerializer>()
                .SingleInstance();

            // Placeholder for queue map; the builder will populate it in Build()
            containerBuilder.Register(_ => new Dictionary<string, (Type MessageType, Type HandlerType)>(StringComparer.OrdinalIgnoreCase))
                .As<IDictionary<string, (Type MessageType, Type HandlerType)>>()
                .SingleInstance();

            // Default EventBus (RabbitMQ). Consumers may override with UseEventBus<T>().
            containerBuilder.Register(sp =>
            {
                var logger = sp.Resolve<ILogger<RabbitMqEventBus>>();
                var ebOptions = sp.Resolve<IOptions<EventBusOptions>>().Value;
                var serializer = sp.Resolve<IMessageSerializer>();
                var map = sp.Resolve<IDictionary<string, (Type MessageType, Type HandlerType)>>();

                if (!string.Equals(ebOptions.Provider, Constants.ProviderRabbitMq, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsupported EventBus provider '{ebOptions.Provider}'. " +
                                                        "Override with LyciaBuilder.UseEventBus<T>() to supply a custom bus.");
                }

                if (string.IsNullOrWhiteSpace(ebOptions.ConnectionString))
                    throw new InvalidOperationException("Lycia:EventBus:ConnectionString is required.");

                return RabbitMqEventBus.Create(
                    logger: logger,
                    queueTypeMap: map,
                    options: ebOptions,
                    serializer: serializer
                );
            }).As<IEventBus>().SingleInstance();

            // Default SagaStore (Redis). Consumers may override with UseSagaStore<T>().
            containerBuilder.Register(sp =>
            {
                var storeOpts = sp.Resolve<IOptions<SagaStoreOptions>>().Value;
                var eventBus = sp.Resolve<IEventBus>();
                var idGen = sp.Resolve<ISagaIdGenerator>();
                var compCoord = sp.Resolve<ISagaCompensationCoordinator>();

                if (!string.Equals(storeOpts.Provider, Constants.ProviderRedis, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsupported SagaStore provider '{storeOpts.Provider}'. " +
                                                        "Override with LyciaBuilder.UseSagaStore<T>() to supply a custom store.");
                }

                var redis = sp.Resolve<IDatabase>();
                return new RedisSagaStore(redis, eventBus, idGen, compCoord, storeOpts);
            }).As<ISagaStore>().InstancePerLifetimeScope();

            RegisterMiddlewareAndPolicies(containerBuilder);

            containerBuilder
                .Register(ctx => new _LfServiceProvider(ctx.Resolve<ILifetimeScope>()))
                .As<IServiceProvider>()
                .SingleInstance();

            containerBuilder
                .RegisterType<_LfServiceScopeFactory>()
                .As<IServiceScopeFactory>()
                .SingleInstance();

            containerBuilder.RegisterType<RabbitMqListener>().AsSelf().SingleInstance().AutoActivate();

            return new LyciaBuilder(containerBuilder, configuration);
        }

        /// <summary>
        /// Adds Lycia and allows inline configuration of LyciaBuilder. 
        /// All Configure* methods are only valid inside this action.
        /// </summary>
        public static LyciaBuilder AddLycia(
            this ContainerBuilder containerBuilder,
            Action<LyciaBuilder> configure,
            IConfiguration configuration)
        {
            var builder = AddLycia(containerBuilder, configuration);
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

        private static void RegisterMiddlewareAndPolicies(ContainerBuilder containerBuilder)
        {
            // Default retry policy (Polly-based). Can be overridden by registering IRetryPolicy before Build().
            containerBuilder.RegisterType<PollyRetryPolicy>().As<IRetryPolicy>().SingleInstance();

            // 1) Ensure default middlewares are available as ISagaMiddleware (idempotent, allow multiple implementations)
            containerBuilder.RegisterType<LoggingMiddleware>().As<ISagaMiddleware>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<RetryMiddleware>().As<ISagaMiddleware>().InstancePerLifetimeScope();

            // 2) Default ordered pipeline: Logging first, then Retry
            //    This will only be used if the host app does NOT call UseSagaMiddleware(Action<>)
            //    because that overload registers its own IReadOnlyList<Type>.
            containerBuilder.RegisterInstance<IReadOnlyList<Type>>(new List<Type>
            {
                typeof(LoggingMiddleware),
                typeof(RetryMiddleware)
            }).As<IReadOnlyList<Type>>().SingleInstance();
        }

        private static void ConfigureRedisEventStore(ContainerBuilder containerBuilder, IConfigurationSection storeSection)
        {
            var storeProvider = storeSection["Provider"] ?? Constants.ProviderRedis;
            if (storeProvider.Equals(Constants.ProviderRedis, StringComparison.OrdinalIgnoreCase))
            {
                var redisConn = storeSection["ConnectionString"];
                if (string.IsNullOrWhiteSpace(redisConn))
                    throw new InvalidOperationException("Lycia:EventStore:ConnectionString is required for Redis provider.");

                containerBuilder.Register(_ => ConnectionMultiplexer.Connect(redisConn!))
                    .As<IConnectionMultiplexer>()
                    .SingleInstance();

                containerBuilder.Register(sp => sp.Resolve<IConnectionMultiplexer>().GetDatabase())
                    .As<IDatabase>()
                    .InstancePerLifetimeScope();
            }
        }

        /// <summary>
        /// Lightweight in-memory setup for tests or samples. Registers in-memory EventBus and SagaStore,
        /// keeps the same fluent builder so you can still add handlers and override pieces.
        /// </summary>
        public static LyciaBuilder AddLyciaInMemory(this ContainerBuilder containerBuilder, IConfiguration? configuration = null)
        {
            configuration ??= new ConfigurationBuilder().AddInMemoryCollection().Build();

            // Bind minimal options with sensible defaults
            var eb = new EventBusOptions
            {
                Provider = "InMemory",
                ApplicationId = configuration["ApplicationId"]
            };
            containerBuilder.RegisterInstance(Options.Create(eb)).As<IOptions<EventBusOptions>>().SingleInstance();

            var ss = new SagaStoreOptions
            {
                Provider = "InMemory",
                ApplicationId = configuration["ApplicationId"],
                LogMaxRetryCount = Constants.LogMaxRetryCount
            };
            containerBuilder.RegisterInstance(Options.Create(ss)).As<IOptions<SagaStoreOptions>>().SingleInstance();

            var so = new SagaOptions
            {
                DefaultIdempotency = true
            };
            containerBuilder.RegisterInstance(Options.Create(so)).As<IOptions<SagaOptions>>().SingleInstance();

            // Core services
            containerBuilder.RegisterType<DefaultSagaIdGenerator>().As<ISagaIdGenerator>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaContextAccessor>().As<ISagaContextAccessor>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaDispatcher>().As<ISagaDispatcher>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaCompensationCoordinator>().As<ISagaCompensationCoordinator>().InstancePerLifetimeScope();

            // Serializer
            containerBuilder.RegisterType<NewtonsoftJsonMessageSerializer>().SingleInstance();
            containerBuilder.RegisterType<AvroMessageSerializer>().SingleInstance();
            containerBuilder.RegisterType<CompositeMessageSerializer>()
                .As<IMessageSerializer>()
                .SingleInstance();

            // In-memory transports/stores
            containerBuilder.Register(sp => new InMemoryEventBus(new Lazy<ISagaDispatcher>(() => sp.Resolve<ISagaDispatcher>())))
                .As<IEventBus>()
                .InstancePerLifetimeScope();

            containerBuilder.RegisterType<InMemorySagaStore>().As<ISagaStore>().InstancePerLifetimeScope();

            // Placeholder queue map – builder will populate in Build()
            containerBuilder.Register(_ => new Dictionary<string, (Type MessageType, Type HandlerType)>(StringComparer.OrdinalIgnoreCase))
                .As<IDictionary<string, (Type MessageType, Type HandlerType)>>()
                .SingleInstance();

            RegisterMiddlewareAndPolicies(containerBuilder);

            return new LyciaBuilder(containerBuilder, configuration);
        }
    }

    /// <summary>
    /// Fluent builder that lets the app override serializer/bus/store and register saga handlers.
    /// </summary>
    public sealed class LyciaBuilder
    {
        private bool _inlineConfigureGate;
        private readonly ContainerBuilder _containerBuilder;
        private readonly IConfiguration _configuration;
        private readonly List<Assembly> _assemblies = new();
        private readonly HashSet<Type> _explicitHandlerTypes = new();
        private readonly Dictionary<string, (Type MessageType, Type HandlerType)> _manualMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _appIdCache;

        internal LyciaBuilder(ContainerBuilder containerBuilder, IConfiguration configuration)
        {
            _containerBuilder = containerBuilder;
            _configuration = configuration;
            _appIdCache = _configuration["ApplicationId"] ?? throw new InvalidOperationException("ApplicationId is not configured.");
        }

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
            _containerBuilder.RegisterType<TSerializer>().As<IMessageSerializer>().SingleInstance();
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
            _containerBuilder.RegisterType<TEventBus>().As<IEventBus>().SingleInstance();
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
            _containerBuilder.RegisterType<TSagaStore>().As<ISagaStore>().SingleInstance();
            return this;
        }

        /// <summary>
        /// Replaces the logging middleware slot with a custom implementation.
        /// </summary>
        public LyciaBuilder UseLoggingMiddleware<TLogging>()
            where TLogging : class, ISagaMiddleware, ILoggingSagaMiddleware
        {
            _containerBuilder.RegisterType<TLogging>().As<ISagaMiddleware>().InstancePerLifetimeScope();

            _containerBuilder.RegisterInstance<IReadOnlyList<Type>>(new List<Type>
            {
                typeof(TLogging),
                typeof(RetryMiddleware)
            }).As<IReadOnlyList<Type>>().SingleInstance();

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
                { typeof(IRetrySagaMiddleware),   typeof(RetryMiddleware)   }
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

            foreach (var t in ordered)
            {
                _containerBuilder.RegisterType(t).As<ISagaMiddleware>().InstancePerLifetimeScope();
            }

            _containerBuilder.RegisterInstance<IReadOnlyList<Type>>(ordered).As<IReadOnlyList<Type>>().SingleInstance();
            return this;

            // Helper to remove a specific ISagaMiddleware implementation mapping
            void RemoveMiddlewareImplementation(Type impl)
            {
                // no-op for ContainerBuilder (duplicates are allowed; caller should avoid)
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

            _containerBuilder.RegisterType(handlerType).InstancePerLifetimeScope();
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
            var asms = markerTypes?.Where(t => t != null).Select(t => t.Assembly).Distinct().ToArray() ?? Array.Empty<Assembly>();
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
                                         (_LyciaHandlerDiscovery.IsSagaHandlerBase(t) || _LyciaHandlerDiscovery.ImplementsAnySagaInterface(t))))
                {
                    _containerBuilder.RegisterType(handlerType).InstancePerLifetimeScope();
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
            if (configure != null)
            {
                var o = new SagaOptions();
                _configuration.GetSection("Lycia:Saga").Bind(o);
                configure(o);
                _containerBuilder.RegisterInstance(Options.Create(o)).As<IOptions<SagaOptions>>().SingleInstance();
            }
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
            if (configure != null)
            {
                var o = new EventBusOptions();
                _configuration.GetSection(EventBusOptions.SectionName).Bind(o);
                configure(o);
                _containerBuilder.RegisterInstance(Options.Create(o)).As<IOptions<EventBusOptions>>().SingleInstance();
            }
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
            if (configure != null)
            {
                var o = new SagaStoreOptions();
                _configuration.GetSection(SagaStoreOptions.SectionName).Bind(o);
                configure(o);
                _containerBuilder.RegisterInstance(Options.Create(o)).As<IOptions<SagaStoreOptions>>().SingleInstance();
            }
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
            if (configure != null)
            {
                var o = new RetryStrategyOptions();
                configure(o);
                _containerBuilder.RegisterInstance(o).As<RetryStrategyOptions>().SingleInstance();
            }
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
            var o = new RetryStrategyOptions();
            _configuration.GetSection("Lycia:Retry").Bind(o);
            _containerBuilder.RegisterInstance(o).As<RetryStrategyOptions>().SingleInstance();
            return this;
        }

        /// <summary>
        /// Configures logging behavior used by the built-in LoggingMiddleware.
        /// Does not change the middleware type; only adjusts its options.
        /// </summary>
        public LyciaBuilder ConfigureLogging(Action<LoggingOptions>? configure)
        {
            EnsureInlineConfigure(nameof(ConfigureLogging));
            if (configure != null)
            {
                var o = new LoggingOptions();
                _configuration.GetSection("Lycia:Logging").Bind(o);
                configure(o);
                _containerBuilder.RegisterInstance(Options.Create(o)).As<IOptions<LoggingOptions>>().SingleInstance();
            }
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

            foreach (var ht in _explicitHandlerTypes)
                _containerBuilder.RegisterType(ht).InstancePerLifetimeScope();

            foreach (var kv in _manualMap)
                discovered[kv.Key] = kv.Value;

            _containerBuilder.RegisterInstance<IDictionary<string, (Type MessageType, Type HandlerType)>>(discovered)
                .As<IDictionary<string, (Type MessageType, Type HandlerType)>>()
                .SingleInstance();

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
                    else if (def == typeof(StartCoordinatedResponsiveSagaHandler<,,>) || def == typeof(CoordinatedResponsiveSagaHandler<,,>))
                    {
                        TryAdd(args[0], messageTypes);
                        TryAdd(args[1], messageTypes);
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

    // adapter tipleri
    internal sealed class _LfServiceProvider : IServiceProvider
    {
        private readonly Autofac.ILifetimeScope _scope;
        public _LfServiceProvider(Autofac.ILifetimeScope scope) { _scope = scope; }
        public object GetService(Type serviceType) => _scope.ResolveOptional(serviceType);
    }

    internal sealed class _LfServiceScope : IServiceScope
    {
        private readonly Autofac.ILifetimeScope _scope;
        public IServiceProvider ServiceProvider { get; }
        public _LfServiceScope(Autofac.ILifetimeScope scope)
        {
            _scope = scope;
            ServiceProvider = new _LfServiceProvider(scope);
        }
        public void Dispose() => _scope.Dispose();
    }

    internal sealed class _LfServiceScopeFactory : IServiceScopeFactory
    {
        private readonly Autofac.ILifetimeScope _root;
        public _LfServiceScopeFactory(Autofac.ILifetimeScope root) { _root = root; }
        public IServiceScope CreateScope() => new _LfServiceScope(_root.BeginLifetimeScope());
    }
}
#endif
