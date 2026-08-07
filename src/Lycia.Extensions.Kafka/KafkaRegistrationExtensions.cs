using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Serializers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lycia.Extensions.Kafka;

/// <summary>Dependency-injection registration for the Lycia Kafka transport.</summary>
public static class KafkaRegistrationExtensions
{
    /// <summary>Replaces the registered Lycia event bus with the Kafka transport.</summary>
    [Obsolete("Use AddLycia(configuration, lycia => lycia.UseTransport().Kafka(configure)) instead.")]
    public static IServiceCollection AddLyciaKafka(
        this IServiceCollection services,
        Action<KafkaEventBusOptions> configure)
    {
        RegisterKafka(services, configure);
        return services;
    }

    /// <summary>Shared registration logic used by both the obsolete direct API and the transport DSL.</summary>
    internal static void RegisterKafka(IServiceCollection services, Action<KafkaEventBusOptions> configure)
    {
        var options = new KafkaEventBusOptions();
        configure(options);
        services.RemoveAll(typeof(IEventBus));
        services.AddSingleton(options);
        services.AddSingleton<IEventBus>(provider => new KafkaEventBus(
            provider.GetRequiredService<IDictionary<string, (Type MessageType, Type HandlerType)>>(),
            options,
            provider.GetRequiredService<IMessageSerializer>()));
    }
}
