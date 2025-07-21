using System.Reflection;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Common;
using Lycia.Saga.Handlers;
using Lycia.Saga.Helpers;
using System.Configuration;


#if NETSTANDARD2_0
using Autofac;
#else
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#endif

namespace Lycia.Saga.Extensions;

/// <summary>
/// Provides extension methods for automatic registration of Saga handlers
/// in the dependency injection container, based on base class or interface detection.
/// Supports both direct type registration and assembly scanning.
/// </summary>
public static class SagaHandlerRegistrationExtensions
{
#if NETSTANDARD2_0
    // -----------------
    // Autofac version --
    // -----------------

    /// <summary>
    /// Registers a single Saga handler type and its interfaces. No extra parameters.
    /// </summary>
    public static void AddSaga(this ContainerBuilder builder, Type? handlerType)
    {
        RegisterSagaHandler(builder, handlerType);
    }

    /// <summary>
    /// Registers multiple Saga handler types and their interfaces. No extra parameters.
    /// </summary>
    public static void AddSagas(this ContainerBuilder builder, params Type?[] handlerTypes)
    {
        foreach (var handlerType in handlerTypes)
            RegisterSagaHandler(builder, handlerType);
    }

    /// <summary>
    /// Scans the current assembly and automatically registers all detected Saga handler types. No extra parameters.
    /// </summary>
    public static void AddSagasFromCurrentAssembly(this ContainerBuilder builder)
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        builder.AddSagasFromAssemblies(callingAssembly);
    }

    /// <summary>
    /// Scans the assemblies of the marker types and registers detected Saga handler types. No extra parameters.
    /// </summary>
    public static void AddSagasFromAssembliesOf(this ContainerBuilder builder, params Type[] markerTypes)
    {
        var assemblies = markerTypes.Select(t => t.Assembly).Distinct().ToArray();
        builder.AddSagasFromAssemblies(assemblies);
    }

    /// <summary>
    /// Scans the provided assemblies and registers detected Saga handler types. No extra parameters.
    /// </summary>
    public static void AddSagasFromAssemblies(this ContainerBuilder builder, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            IEnumerable<Type?> handlerTypes = assembly.GetTypes().Where(t =>
                t is { IsAbstract: false, IsInterface: false } &&
                (IsSagaHandlerBase(t) || ImplementsAnySagaInterface(t))
            );
            foreach (var handlerType in handlerTypes)
            {
                RegisterSagaHandler(builder, handlerType);
            }
        }
    }

    /// <summary>
    /// Registers the handler and its matching interfaces with InstancePerLifetimeScope,
    /// and updates the shared queueTypeMap (and reads appId) from the container at runtime.
    /// </summary>
    private static void RegisterSagaHandler(ContainerBuilder builder, Type? type)
    {
        if (type == null)
            return;

        var allHandlerInterfaces = type.GetInterfaces()
            .Where(i => i.IsGenericType &&
                (
                    i.GetGenericTypeDefinition() == typeof(ISagaHandler<>)
                    || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>)
                    || i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>)
                    || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>)
                    || i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
                    || i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>)
                    || i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)
                ))
            .ToList();

        foreach (var interfaceType in allHandlerInterfaces)
        {
            builder.RegisterType(type)
                .As(interfaceType)
                .InstancePerLifetimeScope()
                .OnActivated(e =>
                {
                    var appId = ConfigurationManager.AppSettings["ApplicationId"];
                    var queueTypeMap = new Dictionary<string, Type>();
                    builder.RegisterInstance(queueTypeMap)
                           .As<Dictionary<string, Type>>()
                           .SingleInstance();

                    var messageTypes = GetMessageTypesFromHandler(type!);
                    foreach (var messageType in messageTypes)
                    {
                        var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, type!, appId);
                        queueTypeMap[routingKey] = messageType;
                    }
                });
        }
    }
#else
    // .NET 6+ implementation (no change)

    public static ILyciaServiceCollection AddSaga(this ILyciaServiceCollection serviceCollection, Type? handlerType)
    {
        var appId = serviceCollection.Configuration!["ApplicationId"] ??
                    throw new InvalidOperationException("ApplicationId is not configured.");

        RegisterSagaHandler(serviceCollection.Services, handlerType);

        if (handlerType != null)
        {
            var messageTypes = GetMessageTypesFromHandler(handlerType);
            foreach (var messageType in messageTypes)
            {
                var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, appId);
                serviceCollection.QueueTypeMap[routingKey] = messageType;
            }
        }

        return serviceCollection;
    }

    public static ILyciaServiceCollection AddSagas(this ILyciaServiceCollection serviceCollection, params Type?[] handlerTypes)
    {
        var appId = serviceCollection.Configuration!["ApplicationId"] ??
                    throw new InvalidOperationException("ApplicationId is not configured.");

        foreach (var handlerType in handlerTypes)
        {
            RegisterSagaHandler(serviceCollection.Services, handlerType);

            if (handlerType != null)
            {
                var messageTypes = GetMessageTypesFromHandler(handlerType);
                foreach (var messageType in messageTypes)
                {
                    var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, appId);
                    serviceCollection.QueueTypeMap[routingKey] = messageType;
                }
            }
        }
        return serviceCollection;
    }

    public static ILyciaServiceCollection AddSagasFromCurrentAssembly(this ILyciaServiceCollection serviceCollection)
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        return serviceCollection.AddSagasFromAssemblies(callingAssembly);
    }

    public static ILyciaServiceCollection AddSagasFromAssembliesOf(this ILyciaServiceCollection serviceCollection,
        params Type[] markerTypes)
    {
        var assemblies = markerTypes.Select(t => t.Assembly).Distinct().ToArray();
        return serviceCollection.AddSagasFromAssemblies(assemblies);
    }

    public static ILyciaServiceCollection AddSagasFromAssemblies(this ILyciaServiceCollection serviceCollection,
        params Assembly[] assemblies)
    {
        var appId = serviceCollection.Configuration!["ApplicationId"] ??
                    throw new InvalidOperationException("ApplicationId is not configured.");

        foreach (var assembly in assemblies)
        {
            IEnumerable<Type?> handlerTypes = assembly.GetTypes().Where(t =>
                t is { IsAbstract: false, IsInterface: false } &&
                (IsSagaHandlerBase(t) || ImplementsAnySagaInterface(t))
            );
            foreach (var handlerType in handlerTypes)
            {
                RegisterSagaHandler(serviceCollection.Services, handlerType);

                if (handlerType == null) continue;
                var messageTypes = GetMessageTypesFromHandler(handlerType);
                foreach (var messageType in messageTypes)
                {
                    var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, appId);
                    serviceCollection.QueueTypeMap[routingKey] = messageType;
                }
            }
        }

        return serviceCollection;
    }

    private static void RegisterSagaHandler(IServiceCollection serviceCollection, Type? type)
    {
        if (type == null)
            return;

        var startReactiveBase = GetGenericBaseType(type, typeof(StartReactiveSagaHandler<>));
        if (startReactiveBase != null)
        {
            var handlerInterfaces = type.GetInterfaces()
                .Where(i =>
                    i.IsGenericType && (
                        i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>) ||
                        i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
                    )
                )
                .ToList();

            foreach (var interfaceType in handlerInterfaces)
            {
                serviceCollection.TryAddScoped(interfaceType, type);
            }

            return;
        }

        var reactiveBase = GetGenericBaseType(type, typeof(ReactiveSagaHandler<>));
        if (reactiveBase != null)
        {
            var handlerInterfaces = type.GetInterfaces()
                .Where(i =>
                    i.IsGenericType && (
                        i.GetGenericTypeDefinition() == typeof(ISagaHandler<>) ||
                        i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
                    )
                )
                .ToList();

            foreach (var interfaceType in handlerInterfaces)
            {
                serviceCollection.TryAddScoped(interfaceType, type);
            }
            return;
        }

        var startCoordinatedBase = GetGenericBaseType(type, typeof(StartCoordinatedSagaHandler<,,>));
        if (startCoordinatedBase != null)
        {
            var handlerInterfaces = type.GetInterfaces()
                .Where(i =>
                    i.IsGenericType && (
                        i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>) ||
                        i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>) ||
                        i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>) ||
                        i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
                    )
                )
                .ToList();

            foreach (var interfaceType in handlerInterfaces)
            {
                serviceCollection.TryAddScoped(interfaceType, type);
            }

            return;
        }

        var coordinatedBase = GetGenericBaseType(type, typeof(CoordinatedSagaHandler<,,>));
        if (coordinatedBase != null)
        {
            var handlerInterfaces = type.GetInterfaces()
                .Where(i =>
                    i.IsGenericType && (
                        i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>) ||
                        i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>) ||
                        i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>) ||
                        i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
                    )
                )
                .ToList();

            foreach (var interfaceType in handlerInterfaces)
            {
                serviceCollection.TryAddScoped(interfaceType, type);
            }
            return;
        }

        var responseBase = GetGenericBaseType(type, typeof(ResponseSagaHandler<,>));
        if (responseBase != null)
        {
            var handlerInterfaces = type.GetInterfaces()
                .Where(i =>
                    i.IsGenericType && (
                        i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>) ||
                        i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)
                    )
                )
                .ToList();

            foreach (var interfaceType in handlerInterfaces)
                serviceCollection.TryAddScoped(interfaceType, type);

            return;
        }

        var sagaInterfaces = GetSagaInterfaces(type);

        if (sagaInterfaces.Count == 0) return;

        foreach (var iFace in sagaInterfaces)
        {
            serviceCollection.TryAddScoped(iFace, type);
        }
    }
#endif

    // ------------- Shared helpers --------------

    public static Dictionary<string, Type> DiscoverQueueTypeMap(string? applicationId, params Assembly[] assemblies)
    {
        var handlerTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                (
                    i.GetGenericTypeDefinition() == typeof(ISagaHandler<>)
                    || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>)
                    || i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>)
                    || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>)
                    || i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
                    || i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>)
                    || i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)
                )))
            .ToList();

        var queueTypeMap = new Dictionary<string, Type>();

        foreach (var handlerType in handlerTypes)
        {
            var messageTypes = GetMessageTypesFromHandler(handlerType);
            foreach (var messageType in messageTypes)
            {
                var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, applicationId);
                queueTypeMap[routingKey] = messageType;
            }
        }

        return queueTypeMap;
    }

    private static List<Type> GetSagaInterfaces(Type type)
    {
        return type.GetInterfaces()
            .Where(i => i.IsGenericType &&
                        (i.GetGenericTypeDefinition() == typeof(ISagaHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)))
            .ToList();
    }

    private static bool IsSagaHandlerBase(Type? type)
    {
        return GetGenericBaseType(type, typeof(StartReactiveSagaHandler<>)) != null
               || GetGenericBaseType(type, typeof(ReactiveSagaHandler<>)) != null
               || GetGenericBaseType(type, typeof(StartCoordinatedSagaHandler<,,>)) != null
               || GetGenericBaseType(type, typeof(CoordinatedSagaHandler<,,>)) != null
               || GetGenericBaseType(type, typeof(ResponseSagaHandler<,>)) != null;
    }

    private static bool ImplementsAnySagaInterface(Type? type)
    {
        if (type == null)
            return false;

        return type.GetInterfaces().Any(i =>
            i.IsGenericType &&
            (i.GetGenericTypeDefinition() == typeof(ISagaHandler<>)
             || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>)
             || i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>)
             || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>)
             || i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
             || i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>)
             || i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)));
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

    private static IEnumerable<Type> GetMessageTypesFromHandler(Type handlerType)
    {
        var messageTypes = new HashSet<Type>();

        var interfaces = handlerType.GetInterfaces()
            .Where(i => i.IsGenericType &&
                        (i.GetGenericTypeDefinition() == typeof(ISagaHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)));

        foreach (var iFace in interfaces)
        {
            var genericArgs = iFace.GetGenericArguments();
            foreach (var arg in genericArgs)
            {
                messageTypes.Add(arg);
            }
        }

        return messageTypes;
    }
}
