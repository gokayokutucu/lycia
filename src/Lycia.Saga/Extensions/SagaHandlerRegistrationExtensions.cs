using System.Reflection;
using System.Runtime.CompilerServices;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Common;
using Lycia.Saga.Handlers;
using Lycia.Saga.Handlers.Abstractions;
using Lycia.Saga.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lycia.Saga.Extensions;

/// <summary>
/// Provides extension methods for automatic registration of Saga handlers
/// in the dependency injection container, based on base class or interface detection.
/// Supports both direct type registration and assembly scanning.
/// </summary>
public static class SagaHandlerRegistrationExtensions
{
    /// <summary>
    /// Registers a single Saga handler class and its relevant interfaces.
    /// </summary>
    /// <param name="serviceCollection">The DI service collection.</param>
    /// <param name="handlerType">Concrete Saga handler class type.</param>
    /// <returns>The updated service collection.</returns>
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

    /// <summary>
    /// Scans the assemblies containing the marker types and registers all detected Saga handlers.
    /// </summary>
    /// <param name="serviceCollection">The DI service collection.</param>
    /// <param name="markerTypes">Marker types from target assemblies.</param>
    /// <returns>The updated service collection.</returns>
    public static ILyciaServiceCollection AddSagasFromAssembliesOf(this ILyciaServiceCollection serviceCollection,
        params Type[] markerTypes)
    {
        var assemblies = markerTypes.Select(t => t.Assembly).Distinct().ToArray();
        return serviceCollection.AddSagasFromAssemblies(assemblies);
    }

    /// <summary>
    /// Scans the provided assemblies and registers all detected Saga handlers by their base types or interfaces.
    /// </summary>
    /// <param name="serviceCollection">The DI service collection.</param>
    /// <param name="assemblies">Assemblies to scan.</param>
    /// <returns>The updated service collection.</returns>
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

    public static Dictionary<string, (Type MessageType, Type HandlerType)> DiscoverQueueTypeMap(string? applicationId,
        params Assembly[] assemblies)
    {
        var handlerTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                (
                    i.GetGenericTypeDefinition() == typeof(ISagaHandler<>) ||
                    i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>) ||
                    i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>) ||
                    i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>) ||
                    i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>) ||
                    i.GetGenericTypeDefinition() == typeof(IResponseSagaHandler<>) ||                    
                    i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>) ||
                    i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)
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
               || GetGenericBaseType(type, typeof(StartCoordinatedSagaHandler<,>)) != null
               || GetGenericBaseType(type, typeof(StartCoordinatedResponsiveSagaHandler<,,>)) != null
               || GetGenericBaseType(type, typeof(CoordinatedResponsiveSagaHandler<,,>)) != null
               || GetGenericBaseType(type, typeof(CoordinatedSagaHandler<,>)) != null;
    }

    /// <summary>
    /// Checks if a type implements any of the known Saga handler interfaces.
    /// </summary>
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

    /// <summary>
    /// Helper to find a direct or indirect generic base class of a given type.
    /// </summary>
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
    /// Extracts all message types from a handler type based on its implemented Saga interfaces.
    /// </summary>
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
                         || i.GetGenericTypeDefinition() == typeof(IResponseSagaHandler<>)
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