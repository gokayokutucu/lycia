using System.Reflection;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers;
using Microsoft.Extensions.DependencyInjection;

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
    /// <param name="services">The DI service collection.</param>
    /// <param name="handlerType">Concrete Saga handler class type.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSagaHandler(this IServiceCollection services, Type? handlerType)
    {
        RegisterSagaHandler(services, handlerType);
        return services;
    }

    /// <summary>
    /// Registers multiple Saga handler classes and their interfaces.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="handlerTypes">Array of handler class types.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSagaHandlers(this IServiceCollection services, params Type?[] handlerTypes)
    {
        foreach (var type in handlerTypes)
            RegisterSagaHandler(services, type);
        return services;
    }

    /// <summary>
    /// Scans the assemblies containing the marker types and registers all detected Saga handlers.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="markerTypes">Marker types from target assemblies.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSagaHandlersFromAssembliesOf(this IServiceCollection services, params Type[] markerTypes)
    {
        var assemblies = markerTypes.Select(t => t.Assembly).Distinct().ToArray();
        return services.AddSagaHandlersFromAssemblies(assemblies);
    }

    /// <summary>
    /// Scans the provided assemblies and registers all detected Saga handlers by their base types or interfaces.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="assemblies">Assemblies to scan.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSagaHandlersFromAssemblies(this IServiceCollection services, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            IEnumerable<Type?> types = assembly.GetTypes().Where(t =>
                t is { IsAbstract: false, IsInterface: false } &&
                (IsSagaHandlerBase(t) || ImplementsAnySagaInterface(t))
            );
            foreach (var type in types)
            {
                RegisterSagaHandler(services, type);
            }
        }
        return services;
    }

    /// <summary>
    /// Registers the handler and its matching interfaces with Scoped lifetime.
    /// </summary>
    private static void RegisterSagaHandler(IServiceCollection services, Type? type)
    {
        if (type == null)
            return;
        
        // Register StartReactiveSagaHandler<T>
        var startReactiveBase = GetGenericBaseType(type, typeof(StartReactiveSagaHandler<>));
        if (startReactiveBase != null)
        {
            var messageType = startReactiveBase.GetGenericArguments()[0];
            services.AddScoped(type);
            services.AddScoped(typeof(ISagaStartHandler<>).MakeGenericType(messageType), type);

            // Optionally register as ISagaCompensationHandler if implemented
            if (typeof(ISagaCompensationHandler<>).MakeGenericType(messageType).IsAssignableFrom(type))
                services.AddScoped(typeof(ISagaCompensationHandler<>).MakeGenericType(messageType), type);
            return;
        }

        // Register ReactiveSagaHandler<T>
        var reactiveBase = GetGenericBaseType(type, typeof(ReactiveSagaHandler<>));
        if (reactiveBase != null)
        {
            var messageType = reactiveBase.GetGenericArguments()[0];
            services.AddScoped(type);
            services.AddScoped(typeof(ISagaHandler<>).MakeGenericType(messageType), type);
            return;
        }

        // Register StartCoordinatedSagaHandler<T, TR, TD>
        var startCoordinatedBase = GetGenericBaseType(type, typeof(StartCoordinatedSagaHandler<,,>));
        if (startCoordinatedBase != null)
        {
            var messageType = startCoordinatedBase.GetGenericArguments()[0];
            var sagaDataType = startCoordinatedBase.GetGenericArguments()[2];
            services.AddScoped(type);
            services.AddScoped(typeof(ISagaStartHandler<,>).MakeGenericType(messageType, sagaDataType), type);
            return;
        }

        // Register CoordinatedSagaHandler<T, TR, TD>
        var coordinatedBase = GetGenericBaseType(type, typeof(CoordinatedSagaHandler<,,>));
        if (coordinatedBase != null)
        {
            var messageType = coordinatedBase.GetGenericArguments()[0];
            var sagaDataType = coordinatedBase.GetGenericArguments()[2];
            services.AddScoped(type);
            services.AddScoped(typeof(ISagaHandler<,>).MakeGenericType(messageType, sagaDataType), type);
            return;
        }

        // Register ResponseSagaHandler<TResponse, TSagaData>
        var responseBase = GetGenericBaseType(type, typeof(ResponseSagaHandler<,>));
        if (responseBase != null)
        {
            var responseType = responseBase.GetGenericArguments()[0];
            services.AddScoped(type);
            services.AddScoped(typeof(ISuccessResponseHandler<>).MakeGenericType(responseType), type);
            services.AddScoped(typeof(IFailResponseHandler<>).MakeGenericType(responseType), type);
            return;
        }

        // Fallback: Register directly for known interfaces
        var sagaInterfaces = type?.GetInterfaces()
            .Where(i => i.IsGenericType &&
                        (i.GetGenericTypeDefinition() == typeof(ISagaHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaHandler<,>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaStartHandler<,>)
                         || i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(ISuccessResponseHandler<>)
                         || i.GetGenericTypeDefinition() == typeof(IFailResponseHandler<>)))
            .ToList();

        if (sagaInterfaces != null && sagaInterfaces.Count != 0 && type != null)
        {
            services.AddScoped(type);
            foreach (var iFace in sagaInterfaces)
            {
                services.AddScoped(iFace, type);
            }
        }
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
}