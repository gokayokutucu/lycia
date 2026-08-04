using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Serializers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lycia.Extensions.Nats;

/// <summary>Dependency-injection registration for the Lycia NATS transport.</summary>
public static class NatsRegistrationExtensions
{
    /// <summary>Replaces the registered Lycia event bus with the NATS transport.</summary>
    public static IServiceCollection AddLyciaNats(
        this IServiceCollection services,
        Action<NatsEventBusOptions> configure)
    {
        var options = new NatsEventBusOptions();
        configure(options);
        services.RemoveAll(typeof(IEventBus));
        services.AddSingleton(options);
        services.AddSingleton<IEventBus>(provider => new NatsEventBus(
            provider.GetRequiredService<IDictionary<string, (Type MessageType, Type HandlerType)>>(),
            options,
            provider.GetRequiredService<IMessageSerializer>()));
        return services;
    }
}
