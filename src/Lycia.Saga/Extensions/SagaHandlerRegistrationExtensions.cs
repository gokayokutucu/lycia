using System.Reflection;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Common;
using Lycia.Saga.Handlers;
using Lycia.Saga.Helpers;
using System.Configuration;
using Lycia.Saga.Configurations;



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
    public static void AddSagasFromCurrentAssembly(this ContainerBuilder builder, LyciaOptions options = null)
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        builder.AddSagasFromAssemblies(options, callingAssembly);
    }

    /// <summary>
    /// Scans the assemblies of the marker types and registers detected Saga handler types. No extra parameters.
    /// </summary>
    public static void AddSagasFromAssembliesOf(this ContainerBuilder builder, LyciaOptions options = null, params Type[] markerTypes)
    {
        var assemblies = markerTypes.Select(t => t.Assembly).Distinct().ToArray();
        builder.AddSagasFromAssemblies(options, assemblies);
    }

    /// <summary>
    /// Scans the provided assemblies and registers detected Saga handler types. No extra parameters.
    /// </summary>
    public static void AddSagasFromAssemblies(this ContainerBuilder builder, LyciaOptions options = null, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            IEnumerable<Type?> handlerTypes = assembly.GetTypes().Where(t =>
                t is { IsAbstract: false, IsInterface: false } &&
                (IsSagaHandlerBase(t) || ImplementsAnySagaInterface(t))
            );
            foreach (var handlerType in handlerTypes)
            {
                RegisterSagaHandler(builder, handlerType, options);
            }
        }
    }

    /// <summary>
    /// Registers the handler and its matching interfaces with InstancePerLifetimeScope,
    /// and updates the shared queueTypeMap (and reads appId) from the container at runtime.
    /// </summary>
    private static void RegisterSagaHandler(ContainerBuilder builder, Type? type, LyciaOptions options = null)
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
                    var appId = options?.ApplicationId ?? throw new InvalidOperationException("ApplicationId is not configured.");
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
    /// <returns>The updated service collection.</returns>//GOP
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
        params Assembly[] assemblies)//GOP
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
