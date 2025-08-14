using System.Reflection;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Common;
using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Lycia.Saga.Helpers;
using System.Configuration;
using Lycia.Saga.Configurations;
using Microsoft.Extensions.Configuration;




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
    public static ContainerBuilder AddSaga(this ContainerBuilder builder, IConfiguration configuration, Type handlerType, IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap)
    {
        var appId = configuration["ApplicationId"] ?? throw new InvalidOperationException("ApplicationId is not configured.");
        if (handlerType is null) throw new ArgumentNullException(nameof(handlerType), "Handler type cannot be null.");

        builder.RegisterType(handlerType).InstancePerLifetimeScope();

        var messageTypes = GetMessageTypesFromHandler(handlerType);
        foreach (var messageType in messageTypes)
        {
            var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, appId);
            queueTypeMap[routingKey] = (messageType, handlerType);
        }
        return builder;
    }

    /// <summary>
    /// Registers multiple Saga handler classes and their interfaces.
    /// </summary>
    /// <param name="serviceCollection">The DI service collection.</param>
    /// <param name="handlerTypes">Array of handler class types.</param>
    /// <returns>The updated service collection.</returns>
    public static ContainerBuilder AddSagas(this ContainerBuilder builder, IConfiguration configuration, IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap, params Type?[] handlerTypes)
    {
        var appId = configuration["ApplicationId"] ?? throw new InvalidOperationException("ApplicationId is not configured.");
        foreach (var handlerType in handlerTypes)
        {
            if (handlerType is null) continue;

            builder.RegisterType(handlerType).InstancePerLifetimeScope();

            var messageTypes = GetMessageTypesFromHandler(handlerType);
            foreach (var messageType in messageTypes)
            {
                var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, appId);
                queueTypeMap[routingKey] = (messageType, handlerType);
            }
        }
        return builder;
    }

    /// <summary>
    /// Scans the assembly from which this method is called and automatically registers all Saga handler types.
    /// This is useful for registering all handlers in the current project or module without specifying marker types explicitly.
    /// </summary>
    /// <param name="serviceCollection">The DI service collection wrapper.</param>
    /// <returns>The updated service collection.</returns>
    public static ContainerBuilder AddSagasFromCurrentAssembly(this ContainerBuilder builder, IConfiguration configuration, IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap)
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        return builder.AddSagasFromAssemblies(configuration, queueTypeMap, callingAssembly);
    }

    public static ContainerBuilder AddSagasFromAssembliesOf(this ContainerBuilder builder, IConfiguration configuration, IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap, params Type[] markerTypes)
    {
        var assemblies = markerTypes.Select(t => t.Assembly).Distinct().ToArray();
        return builder.AddSagasFromAssemblies(configuration, queueTypeMap, assemblies);
    }

    public static ContainerBuilder AddSagasFromAssemblies(this ContainerBuilder builder, IConfiguration configuration, IDictionary<string, (Type MessageType, Type HandlerType)> queueTypeMap, params Assembly[] assemblies)
    {
        var appId = configuration["ApplicationId"] ?? throw new InvalidOperationException("ApplicationId is not configured.");

        foreach (var assembly in assemblies)
        {
            IEnumerable<Type?> handlerTypes = assembly.GetTypes().Where(t =>
                t is { IsAbstract: false, IsInterface: false } &&
                (IsSagaHandlerBase(t) || ImplementsAnySagaInterface(t))
            );
            foreach (var handlerType in handlerTypes)
            {
                if (handlerType == null) continue;

                builder.RegisterType(handlerType).InstancePerLifetimeScope();

                var messageTypes = GetMessageTypesFromHandler(handlerType);
                foreach (var messageType in messageTypes)
                {
                    var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, appId);
                    queueTypeMap[routingKey] = (messageType, handlerType);
                }
            }
        }
        return builder;
    }
#else
    public static ILyciaServiceCollection AddSaga(this ILyciaServiceCollection serviceCollection, Type? handlerType)
    {
        var appId = serviceCollection.Configuration!["ApplicationId"] ??
                    throw new InvalidOperationException("ApplicationId is not configured.");

        if (handlerType is null)
            throw new ArgumentNullException(nameof(handlerType), "Handler type cannot be null.");

        serviceCollection.Services.AddScoped(handlerType);

        var messageTypes = GetMessageTypesFromHandler(handlerType);
        foreach (var messageType in messageTypes)
        {
            var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, appId);
            serviceCollection.QueueTypeMap[routingKey] = (messageType, handlerType);
        }
        return serviceCollection;
    }

    /// <summary>
    /// Registers multiple Saga handler classes and their interfaces.
    /// </summary>
    /// <param name="serviceCollection">The DI service collection.</param>
    /// <param name="handlerTypes">Array of handler class types.</param>
    /// <returns>The updated service collection.</returns>
    public static ILyciaServiceCollection AddSagas(this ILyciaServiceCollection serviceCollection,
        params Type?[] handlerTypes)
    {
        var appId = serviceCollection.Configuration!["ApplicationId"] ??
                    throw new InvalidOperationException("ApplicationId is not configured.");

        foreach (var handlerType in handlerTypes)
        {
            if(handlerType is null) continue;
            
            serviceCollection.Services.AddScoped(handlerType);

            var messageTypes = GetMessageTypesFromHandler(handlerType);
            foreach (var messageType in messageTypes)
            {
                var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, appId);
                serviceCollection.QueueTypeMap[routingKey] = (messageType, handlerType);
            }
        }

        return serviceCollection;
    }


    /// <summary>
    /// Scans the assembly from which this method is called and automatically registers all Saga handler types.
    /// This is useful for registering all handlers in the current project or module without specifying marker types explicitly.
    /// </summary>
    /// <param name="serviceCollection">The DI service collection wrapper.</param>
    /// <returns>The updated service collection.</returns>
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
                if (handlerType == null) continue;
                
                serviceCollection.Services.AddScoped(handlerType);
                
                var messageTypes = GetMessageTypesFromHandler(handlerType);
                foreach (var messageType in messageTypes)
                {
                    var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, appId);
                    serviceCollection.QueueTypeMap[routingKey] = (messageType, handlerType);
                }
            }
        }

        return serviceCollection;
    }
#endif
    public static Dictionary<string, (Type MessageType, Type HandlerType)> DiscoverQueueTypeMap(string? applicationId,
        params Assembly[] assemblies)
    {
        var handlerTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false } &&
                        ImplementsAnySagaInterface(t))
            .ToList();

        var queueTypeMap = new Dictionary<string, (Type MessageType, Type HandlerType)>();

        foreach (var handlerType in handlerTypes)
        {
            var messageTypes = GetMessageTypesFromHandler(handlerType);
            foreach (var messageType in messageTypes)
            {
                var routingKey = MessagingNamingHelper.GetRoutingKey(messageType, handlerType, applicationId);
                queueTypeMap[routingKey] = (messageType, handlerType);
            }
        }

        return queueTypeMap;
    }

    /// <summary>
    /// Checks if a type is based on a known SagaHandler base class.
    /// </summary>
    private static bool IsSagaHandlerBase(Type? type)
    {
        return GetGenericBaseType(type, typeof(StartReactiveSagaHandler<>)) != null
               || GetGenericBaseType(type, typeof(ReactiveSagaHandler<>)) != null
               || GetGenericBaseType(type, typeof(StartCoordinatedSagaHandler<,>)) != null
               || GetGenericBaseType(type, typeof(StartCoordinatedResponsiveSagaHandler<,,>)) != null
               || GetGenericBaseType(type, typeof(CoordinatedResponsiveSagaHandler<,,>)) != null
               || GetGenericBaseType(type, typeof(CoordinatedSagaHandler<,>)) != null;
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
             || i.GetGenericTypeDefinition() == typeof(IResponseSagaHandler<>)
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

    /// <summary>
    /// Extracts all message types from a handler type based on its implemented Saga interfaces and known base classes.
    /// </summary>
    private static IEnumerable<Type> GetMessageTypesFromHandler(Type handlerType)
    {
        var messageTypes = new HashSet<Type>();

        // 1) From interfaces
        var interfaceArgs = handlerType.GetInterfaces()
            .Where(i => i.IsGenericType &&
                        (i.GetGenericTypeDefinition() == typeof(ISagaHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(IResponseSagaHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)))
            .SelectMany(i => i.GetGenericArguments());

        foreach (var arg in interfaceArgs)
        {
            if (typeof(IMessage).IsAssignableFrom(arg) && !typeof(SagaData).IsAssignableFrom(arg))
                messageTypes.Add(arg);
        }

        // 2) From known generic base classes (walk up the inheritance chain)
        Type? current = handlerType;
        while (current != null && current != typeof(object))
        {
            if (current.IsGenericType)
            {
                var def = current.GetGenericTypeDefinition();
                var args = current.GetGenericArguments();

                if (def == typeof(StartReactiveSagaHandler<>) || def == typeof(ReactiveSagaHandler<>))
                {
                    // [TMessage]
                    TryAddMessage(args[0], messageTypes);
                }
                else if (def == typeof(StartCoordinatedSagaHandler<,>) || def == typeof(CoordinatedSagaHandler<,>))
                {
                    // [TMessage, TSagaData]
                    TryAddMessage(args[0], messageTypes);
                }
                else if (def == typeof(StartCoordinatedResponsiveSagaHandler<,,>) || def == typeof(CoordinatedResponsiveSagaHandler<,,>))
                {
                    // [TMessage, TResponse, TSagaData] => include both message and response
                    TryAddMessage(args[0], messageTypes);
                    TryAddMessage(args[1], messageTypes);
                }
            }

            current = current.BaseType;
        }

        return messageTypes;

        static void TryAddMessage(Type candidate, HashSet<Type> set)
        {
            if (typeof(IMessage).IsAssignableFrom(candidate) && !typeof(SagaData).IsAssignableFrom(candidate))
            {
                set.Add(candidate);
            }
        }
    }

}
