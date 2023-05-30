using System.Reflection;
using Lycia.Dapr.EventBus.Abstractions;
using Lycia.Dapr.Messages.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Dapr.EventBus;

public class DaprEventBus : IEventBus
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public DaprEventBus(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    ///<inheritdoc/>
    public Dictionary<string, IEventHandler> Topics { get; } = new();

    public void Subscribe<T>(Assembly assembly, string? prefix = null, string? suffix = null) where T : IEventHandler
    {
        var eventHandlerType = typeof(T);

        var eventHandlerInterface = eventHandlerType.GetInterfaces()
               .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>));

        if (eventHandlerInterface is null)
            throw new InvalidOperationException();

        var eventType = eventHandlerInterface.GetGenericArguments()[0];

        var eventName = GetEventName(eventType, prefix, suffix);
        var eventHandler = CreateEventHandlerInstance<T>(eventHandlerType);

        Topics.Add(eventName, eventHandler);
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

    private IEventHandler CreateEventHandlerInstance<T>(Type eventHandlerType) where T : IEventHandler
    {
        using var scope = _serviceScopeFactory.CreateScope();

        return scope.ServiceProvider.GetRequiredService<T>();

        //if (eventHandlerType.IsGenericType)
        //{
        //    var eventType = eventHandlerType.GetInterfaces()
        //        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
        //        ?.GetGenericArguments()[0];

        //    var handlerInstance = Activator.CreateInstance(eventHandlerType.MakeGenericType(eventType));
        //    return (IEventHandler)handlerInstance;
        //}
        //else
        //{
        //    var handlerInstance = Activator.CreateInstance(eventHandlerType);
        //    return (IEventHandler)handlerInstance;
        //}
    }
}