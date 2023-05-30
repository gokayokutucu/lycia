using System.Reflection;
using Lycia.Dapr.EventBus.Abstractions;
using Lycia.Dapr.Messages.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Dapr.EventBus;

public class DaprEventBus : IEventBus
{
    private readonly IServiceProvider _serviceProvider;

    public DaprEventBus(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    ///<inheritdoc/>
    public Dictionary<string, IEventHandler> Topics { get; } = new();

    public void Subscribe(Assembly assembly, string? prefix = null, string? suffix = null)
    {
        var eventHandlerTypes = assembly.GetTypes()
            .Where(type => typeof(IEventHandler).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
            .ToList();

        foreach (var eventHandlerType in eventHandlerTypes)
        {
            var eventTypes = GetEventTypes(eventHandlerType);
            foreach (var eventType in eventTypes)
            {
                var eventName = GetEventName(eventType, prefix, suffix);
                var eventHandler = CreateEventHandlerInstance(eventHandlerType);

                Topics.Add(eventName, eventHandler);
            }
        }
    }

    private List<Type> GetEventTypes(Type eventHandlerType)
    {
        var eventHandlerInterface = eventHandlerType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>));

        if (eventHandlerInterface != null)
        {
            var eventType = eventHandlerInterface.GetGenericArguments()[0];
            return new List<Type> { eventType };
        }

        return new List<Type>();
    }

    private string GetEventName(Type eventType, string? prefix, string? suffix)
    {
        var eventName = eventType.Name;
        if (!string.IsNullOrEmpty(prefix))
            eventName = $"{prefix}_{eventName}";
        if (!string.IsNullOrEmpty(suffix))
            eventName = $"{eventName}_{suffix}";

        return eventName;
    }

    private IEventHandler CreateEventHandlerInstance(Type eventHandlerType)
    {
        if (eventHandlerType.IsGenericType)
        {
            var eventType = eventHandlerType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
                ?.GetGenericArguments()[0];

            using (var scope = _serviceProvider.CreateScope())
            {
                var handlerInstance = scope.ServiceProvider.GetRequiredService(eventHandlerType.MakeGenericType(eventType));
                return (IEventHandler)handlerInstance;
            }
        }
        else
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var handlerInstance = scope.ServiceProvider.GetRequiredService(eventHandlerType);
                return (IEventHandler)handlerInstance;
            }
        }
    }


}