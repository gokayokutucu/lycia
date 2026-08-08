// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Listener;
using Lycia.Observability;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Serializers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lycia.Extensions.RabbitMq;

/// <summary>Dependency-injection registration for the Lycia RabbitMQ transport.</summary>
public static class RabbitMqRegistrationExtensions
{
    /// <summary>
    /// Registers the RabbitMQ event bus and background listener. Call after <c>AddLycia(...)</c>;
    /// connection settings are read from <c>Lycia:EventBus</c> (<see cref="EventBusOptions"/>).
    /// </summary>
    [Obsolete("Use AddLycia(configuration, lycia => lycia.UseTransport().RabbitMq()) instead.")]
    public static IServiceCollection AddLyciaRabbitMq(this IServiceCollection services)
    {
        RegisterRabbitMq(services);
        return services;
    }

    /// <summary>Shared registration logic used by both the obsolete direct API and the transport DSL.</summary>
    internal static void RegisterRabbitMq(IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        services.RemoveAll(typeof(IEventBus));
        services.AddSingleton<IEventBus>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RabbitMqEventBus>>();
            var registrationLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("LyciaRegistration");
            var ebOptions = sp.GetRequiredService<IOptions<EventBusOptions>>().Value;
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var map = sp.GetRequiredService<IDictionary<string, (Type MessageType, Type HandlerType)>>();

            if (string.IsNullOrWhiteSpace(ebOptions.ConnectionString))
                throw new InvalidOperationException("Lycia:EventBus:ConnectionString is required.");

            try
            {
                return RabbitMqEventBus.CreateAsync(
                    logger: logger,
                    queueTypeMap: map,
                    options: ebOptions,
                    serializer: serializer
                ).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                registrationLogger.LogError(ex,
                    "Lycia failed to connect to RabbitMQ while initializing the event bus. Check Lycia:EventBus settings.");
                throw new InvalidOperationException(
                    "Lycia was unable to initialize the RabbitMQ event bus. See inner exception for details.",
                    ex);
            }
        });

#if NET8_0_OR_GREATER
        services.AddHostedService<RabbitMqListener>();
#elif NETSTANDARD2_0
        services.AddSingleton<RabbitMqListener>(sp =>
        {
            var listener = new RabbitMqListener(
                sp,
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<ILogger<RabbitMqListener>>(),
                sp.GetRequiredService<IMessageSerializer>(),
                sp.GetRequiredService<LyciaActivitySourceHolder>()
            );
            listener.Start();
            return listener;
        });
#endif
    }
}
