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

    public void Subscribe<T>(string? prefix = null, string? suffix = null) where T : IEventHandler
    {
        var eventHandlerType = typeof(T);

        var assemblies = AppDomain.CurrentDomain.GetAssemblies().ToList();

        var eventHandlers = assemblies
           .SelectMany(assembly => assembly.GetTypes())
           .Where(type => type.IsGenericType && typeof(IEventHandler<>).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
           .ToList();

        foreach (var eventHandler in eventHandlers)
        {
            var eventType = eventHandler.GetGenericArguments()[0];

            var eventName = GetEventName(eventType, prefix, suffix);
            // var eventHandler2 = GetEventHandler<T>(eventHandlerType);

            Topics.Add(eventName, null);
        }

        //var eventHandlerInterface = eventHandlerType.GetInterfaces()
        //       .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>));

        //if (eventHandlerInterface is null)
        //    throw new InvalidOperationException();

        //var eventType = eventHandlerInterface.GetGenericArguments()[0];

        //var eventName = GetEventName(eventType, prefix, suffix);
        //var eventHandler = CreateEventHandlerInstance<T>(eventHandlerType);

        //   Topics.Add(eventName, eventHandler);
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

    private IEventHandler<T> GetEventHandler<T>(Type eventHandlerType) where T : IEvent
    {
        using var scope = _serviceScopeFactory.CreateScope();

        return (IEventHandler<T>)scope.ServiceProvider.GetRequiredService(eventHandlerType);
    }

    private void InvokeGenericMethod<T>(object target, string methodName, T eventData) where T : IEvent
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        var genericMethod = method.MakeGenericMethod(typeof(T));
        genericMethod.Invoke(target, new object[] { eventData });
    }
}