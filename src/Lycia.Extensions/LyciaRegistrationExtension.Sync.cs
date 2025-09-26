#if NETSTANDARD2_0
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Listener;
using Lycia.Extensions.Serialization;
using Lycia.Extensions.Stores;
using Lycia.Infrastructure.Compensating;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Middleware;
using Lycia.Infrastructure.Retry;
using Lycia.Infrastructure.Stores;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Common;
using Lycia.Saga.Configurations;
using Lycia.Saga.Extensions;
using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Lycia.Saga.Helpers;
using Lycia.Saga.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Retry;
using StackExchange.Redis;
using System.Reflection;

namespace Lycia.Extensions
{
    public static class LyciaRegistrationExtensions
    {
        public static LyciaBuilder AddLycia(this ContainerBuilder containerBuilder, IConfiguration configuration)
        {
            var rootAppId = configuration["ApplicationId"];
            var commonTtlSeconds = configuration.GetValue<int?>("Lycia:CommonTTL");
            var storeSection = configuration.GetSection(SagaStoreOptions.SectionName);

            var eb = new EventBusOptions();
            configuration.GetSection(EventBusOptions.SectionName).Bind(eb);
            eb.Provider = string.IsNullOrWhiteSpace(eb.Provider) ? Constants.ProviderRabbitMq : eb.Provider;
            eb.ApplicationId = eb.ApplicationId ?? rootAppId;
            if (commonTtlSeconds is > 0)
            {
                var tt = TimeSpan.FromSeconds(commonTtlSeconds.Value);
                eb.MessageTTL = eb.MessageTTL ?? tt;
                eb.DeadLetterQueueMessageTTL = eb.DeadLetterQueueMessageTTL ?? tt;
            }
            containerBuilder.RegisterInstance(Options.Create(eb)).As<IOptions<EventBusOptions>>().SingleInstance();

            ConfigureRedisEventStore(containerBuilder, storeSection);
            var ss = new SagaStoreOptions();
            configuration.GetSection(SagaStoreOptions.SectionName).Bind(ss);
            ss.Provider = string.IsNullOrWhiteSpace(ss.Provider) ? Constants.ProviderRedis : ss.Provider;
            if (ss.LogMaxRetryCount <= 0) ss.LogMaxRetryCount = Constants.LogMaxRetryCount;
            ss.ApplicationId = ss.ApplicationId ?? rootAppId;
            if (commonTtlSeconds is > 0)
            {
                var tt = TimeSpan.FromSeconds(commonTtlSeconds.Value);
                ss.StepLogTtl = ss.StepLogTtl ?? tt;
            }
            containerBuilder.RegisterInstance(Options.Create(ss)).As<IOptions<SagaStoreOptions>>().SingleInstance();

            var so = new SagaOptions();
            configuration.GetSection("Lycia:Saga").Bind(so);
            so.DefaultIdempotency = true;
            containerBuilder.RegisterInstance(Options.Create(so)).As<IOptions<SagaOptions>>().SingleInstance();

            // Retry options (IOptions<RetryStrategyOptions>) kaydı
            var retrySection = configuration.GetSection("Lycia:Retry");
            var retryOpts = new RetryStrategyOptions();
            retrySection.Bind(retryOpts);
            containerBuilder.RegisterInstance(Options.Create(retryOpts)).As<IOptions<RetryStrategyOptions>>().SingleInstance();


            containerBuilder.RegisterType<DefaultSagaIdGenerator>().As<ISagaIdGenerator>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaDispatcher>().As<ISagaDispatcher>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaCompensationCoordinator>().As<ISagaCompensationCoordinator>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaContextAccessor>().As<ISagaContextAccessor>().InstancePerLifetimeScope();

            containerBuilder.RegisterType<NewtonsoftJsonMessageSerializer>().As<IMessageSerializer>().SingleInstance();

            containerBuilder.Register(_ => new Dictionary<string, (Type MessageType, Type HandlerType)>(StringComparer.OrdinalIgnoreCase))
                .As<IDictionary<string, (Type MessageType, Type HandlerType)>>()
                .SingleInstance();

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

        public static LyciaBuilder AddLycia(this ContainerBuilder containerBuilder, Action<LyciaBuilder> configure, IConfiguration configuration)
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
            containerBuilder.RegisterType<PollyRetryPolicy>().As<Lycia.Infrastructure.Retry.IRetryPolicy>().SingleInstance();

            containerBuilder.RegisterType<LoggingMiddleware>().As<ISagaMiddleware>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<RetryMiddleware>().As<ISagaMiddleware>().InstancePerLifetimeScope();

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

        public static LyciaBuilder AddLyciaInMemory(this ContainerBuilder containerBuilder, IConfiguration? configuration = null)
        {
            configuration ??= new ConfigurationBuilder().AddInMemoryCollection().Build();

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

            containerBuilder.RegisterType<DefaultSagaIdGenerator>().As<ISagaIdGenerator>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaContextAccessor>().As<ISagaContextAccessor>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaDispatcher>().As<ISagaDispatcher>().InstancePerLifetimeScope();
            containerBuilder.RegisterType<SagaCompensationCoordinator>().As<ISagaCompensationCoordinator>().InstancePerLifetimeScope();

            containerBuilder.RegisterType<NewtonsoftJsonMessageSerializer>().As<IMessageSerializer>().SingleInstance();

            containerBuilder.Register(sp => new InMemoryEventBus(new Lazy<ISagaDispatcher>(() => sp.Resolve<ISagaDispatcher>())))
                .As<IEventBus>()
                .InstancePerLifetimeScope();

            containerBuilder.RegisterType<InMemorySagaStore>().As<ISagaStore>().InstancePerLifetimeScope();

            containerBuilder.Register(_ => new Dictionary<string, (Type MessageType, Type HandlerType)>(StringComparer.OrdinalIgnoreCase))
                .As<IDictionary<string, (Type MessageType, Type HandlerType)>>()
                .SingleInstance();

            RegisterMiddlewareAndPolicies(containerBuilder);
            return new LyciaBuilder(containerBuilder, configuration);
        }
    }

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

        public LyciaBuilder UseMessageSerializer<TSerializer>() where TSerializer : class, IMessageSerializer
        {
            _containerBuilder.RegisterType<TSerializer>().As<IMessageSerializer>().SingleInstance();
            return this;
        }

        public LyciaBuilder UseEventBus<TEventBus>() where TEventBus : class, IEventBus
        {
            _containerBuilder.RegisterType<TEventBus>().As<IEventBus>().SingleInstance();
            return this;
        }

        public LyciaBuilder UseSagaStore<TSagaStore>() where TSagaStore : class, ISagaStore
        {
            _containerBuilder.RegisterType<TSagaStore>().As<ISagaStore>().SingleInstance();
            return this;
        }

        public LyciaBuilder UseSagaMiddleware(Action<SagaMiddlewareOptions>? configure)
        {
            if (configure == null) return this;
            var options = new SagaMiddlewareOptions();
            configure(options);

            var slotMap = new Dictionary<Type, Type>
            {
                { typeof(ILoggingSagaMiddleware), typeof(LoggingMiddleware) },
                { typeof(IRetrySagaMiddleware),   typeof(RetryMiddleware)   }
            };

            var extras = new List<Type>();

            foreach (var t in options.Middlewares)
            {
                if (t == null) continue;

                if (typeof(ILoggingSagaMiddleware).IsAssignableFrom(t))
                {
                    slotMap[typeof(ILoggingSagaMiddleware)] = t;
                    RemoveMiddlewareImplementation(typeof(LoggingMiddleware));
                }
                else if (typeof(IRetrySagaMiddleware).IsAssignableFrom(t))
                {
                    slotMap[typeof(IRetrySagaMiddleware)] = t;
                    RemoveMiddlewareImplementation(typeof(RetryMiddleware));
                }
                else if (typeof(ISagaMiddleware).IsAssignableFrom(t))
                {
                    if (!extras.Contains(t)) extras.Add(t);
                }
                else
                {
                    throw new InvalidOperationException($"Type {t.FullName} does not implement ISagaMiddleware.");
                }
            }

            var ordered = new List<Type>
            {
                slotMap[typeof(ILoggingSagaMiddleware)],
                slotMap[typeof(IRetrySagaMiddleware)]
            };

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

            void RemoveMiddlewareImplementation(Type impl)
            {
                // no-op for ContainerBuilder (duplicates are allowed; caller should avoid)
            }
        }

        public LyciaBuilder AddHandlersFrom(params Assembly[]? assemblies)
        {
            if (assemblies == null) return this;

            foreach (var a in assemblies.Where(x => x != null))
                _assemblies.Add(a);

            return this;
        }

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

        public LyciaBuilder AddSagasFromCurrentAssembly()
        {
            var calling = Assembly.GetCallingAssembly();
            return AddSagasFromAssemblies(calling);
        }

        public LyciaBuilder AddSagasFromAssembliesOf(params Type[] markerTypes)
        {
            var asms = markerTypes?.Where(t => t != null).Select(t => t.Assembly).Distinct().ToArray() ?? Array.Empty<Assembly>();
            return AddSagasFromAssemblies(asms);
        }

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

        public LyciaBuilder ConfigureRetry()
        {
            EnsureInlineConfigure(nameof(ConfigureSaga));
            var o = new RetryStrategyOptions();
            _configuration.GetSection("Lycia:Retry").Bind(o);
            _containerBuilder.RegisterInstance(o).As<RetryStrategyOptions>().SingleInstance();
            return this;
        }

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
