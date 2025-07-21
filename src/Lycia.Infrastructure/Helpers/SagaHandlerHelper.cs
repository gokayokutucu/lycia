using Lycia.Infrastructure.Extensions;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Infrastructure.Helpers;

public static class SagaHandlerHelper
{
    /// <summary>
    /// Finds all candidate compensation handlers for a given message type.
    /// Supports both interface-based and base class-based registrations (hybrid model).
    /// </summary>
    public static IEnumerable<object?> FindCompensationHandlers(IServiceProvider serviceProvider, Type messageType)
    {
        var handlers = new List<object?>();
        // Find handlers registered via all three interfaces
        handlers.AddRange(GetInterfaceRegisteredHandlers(
            serviceProvider,
            messageType,
            [
                typeof(ISagaStartHandler<>),
                typeof(ISagaHandler<>),
                typeof(ISagaCompensationHandler<>)
            ]
        ));
        handlers.AddRange(GetBaseClassRegisteredHandlers(serviceProvider, messageType));

        var knownBaseTypes = new[]
        {
            typeof(ReactiveSagaHandler<>),
            typeof(StartReactiveSagaHandler<>),
            typeof(CoordinatedSagaHandler<,,>),
            typeof(StartCoordinatedSagaHandler<,,>)
        };
        var knownInterfaces = new[]
        {
            typeof(ISagaCompensationHandler<>)
        };

        // Filter by actual type compatibility and remove duplicates
        var filtered = handlers.Where(h => IsCandidateHandler(h, messageType, knownBaseTypes, knownInterfaces)).DistinctByKey(h => h.GetType());
        return filtered;
    }

    /// <summary>
    /// Finds all candidate saga handlers (ISagaHandler) for a given message type.
    /// </summary>
    public static IEnumerable<object?> FindSagaHandlers(IServiceProvider serviceProvider, Type messageType)
    {
        var handlers = new List<object?>();
        // Only ISagaHandler interface
        handlers.AddRange(GetInterfaceRegisteredHandlers(
            serviceProvider,
            messageType,
            [
                typeof(ISagaHandler<>)
            ]
        ));
        handlers.AddRange(GetBaseClassRegisteredHandlers(serviceProvider, messageType));

        var knownBaseTypes = new[]
        {
            typeof(ReactiveSagaHandler<>),
            typeof(CoordinatedSagaHandler<,,>)
        };
        var knownInterfaces = new[]
        {
            typeof(ISagaHandler<>)
        };

        var filtered = handlers.Where(h => IsCandidateHandler(h, messageType, knownBaseTypes, knownInterfaces)).DistinctByKey(h => h.GetType());
        return filtered;
    }

    /// <summary>
    /// Finds all candidate saga start handlers (ISagaStartHandler) for a given message type.
    /// </summary>
    public static IEnumerable<object?> FindSagaStartHandlers(IServiceProvider serviceProvider, Type messageType)
    {
        var handlers = new List<object?>();
        // Only ISagaStartHandler interface
        handlers.AddRange(GetInterfaceRegisteredHandlers(
            serviceProvider,
            messageType,
            [
                typeof(ISagaStartHandler<>)
            ]
        ));
        handlers.AddRange(GetBaseClassRegisteredHandlers(serviceProvider, messageType));

        var knownBaseTypes = new[]
        {
            typeof(StartReactiveSagaHandler<>),
            typeof(StartCoordinatedSagaHandler<,,>)
        };
        var knownInterfaces = new[]
        {
            typeof(ISagaStartHandler<>)
        };

        var filtered = handlers.Where(h => IsCandidateHandler(h, messageType, knownBaseTypes, knownInterfaces)).DistinctByKey(h => h.GetType());
        return filtered;
    }

    /// <summary>
    /// Gets handlers registered via specified saga handler interfaces.
    /// </summary>
    private static IEnumerable<object?> GetInterfaceRegisteredHandlers(IServiceProvider provider, Type messageType, IEnumerable<Type> interfaceTypes)
    {
        return interfaceTypes.Select(interfaceType => interfaceType.MakeGenericType(messageType)).SelectMany(provider.GetServices);
    }

    /// <summary>
    /// Gets handlers registered directly as a base type (abstract handler).
    /// </summary>
    private static IEnumerable<object?> GetBaseClassRegisteredHandlers(IServiceProvider provider, Type messageType)
    {
        var knownBaseTypes = new[]
        {
            typeof(ReactiveSagaHandler<>),
            typeof(StartReactiveSagaHandler<>),
            typeof(CoordinatedSagaHandler<,,>),
            typeof(StartCoordinatedSagaHandler<,,>)
        };

        foreach (var baseType in knownBaseTypes)
        {
            if (baseType.GetGenericArguments().Length == 1)
            {
                // Choreography-style handlers
                var constructed = baseType.MakeGenericType(messageType);
                foreach (var s in provider.GetServices(constructed))
                    yield return s;
            }
            else if (baseType.GetGenericArguments().Length == 3)
            {
                // Coordination-style handlers: need to scan all registered objects
                foreach (var instance in provider.GetServices(typeof(object)))
                {
                    if (IsBaseTypeCompatible(instance, baseType, messageType))
                        yield return instance;
                }
            }
        }
    }

    /// <summary>
    /// Determines if an instance is a valid handler for messageType based on known base types and interfaces.
    /// </summary>
    private static bool IsCandidateHandler(object? handler, Type messageType, Type[] knownBaseTypes, Type[] knownInterfaces)
    {
        var type = handler?.GetType();

        // Check interfaces
        if (type != null && type.GetInterfaces().Any(i =>
                i.IsGenericType &&
                knownInterfaces.Any(knownInterface => i.GetGenericTypeDefinition() == knownInterface) &&
                i.GetGenericArguments()[0] == messageType))
            return true;

        // Check base class chain
        var baseType = type?.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType)
            {
                var genericDef = baseType.GetGenericTypeDefinition();

                if (knownBaseTypes.Contains(genericDef))
                {
                    var args = baseType.GetGenericArguments();
                    if (args.Length > 0 && args[0] == messageType)
                        return true;
                }
            }
            baseType = baseType.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Checks if the given instance's base type chain matches the known handler base types.
    /// </summary>
    private static bool IsBaseTypeCompatible(object? instance, Type baseTypeDef, Type messageType)
    {
        var type = instance?.GetType();
        var baseType = type?.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == baseTypeDef)
            {
                var genericArgs = baseType.GetGenericArguments();
                if (genericArgs.Length > 0 && genericArgs[0] == messageType)
                    return true;
            }
            baseType = baseType.BaseType;
        }
        return false;
    }
}