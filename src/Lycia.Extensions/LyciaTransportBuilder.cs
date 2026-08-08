// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lycia.Extensions;

/// <summary>
/// Fluent transport-selection DSL reached via <see cref="LyciaBuilder.UseTransport"/>.
/// Lycia.Extensions defines only this builder and the in-process <see cref="InMemory"/> provider;
/// transport packages (Lycia.Extensions.RabbitMq, Lycia.Extensions.Nats, Lycia.Extensions.Kafka)
/// contribute their own providers as extension methods on this type, so Lycia.Extensions never
/// takes a compile-time dependency on a specific transport package.
/// </summary>
public sealed class LyciaTransportBuilder
{
    /// <summary>The underlying service collection transport providers register into.</summary>
    public IServiceCollection Services { get; }

    /// <summary>The configuration Lycia was bootstrapped with.</summary>
    public IConfiguration Configuration { get; }

    internal LyciaTransportBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;
    }

    /// <summary>
    /// Marks <paramref name="providerName"/> as the selected transport provider. Transport packages must
    /// call this before registering their <c>IEventBus</c> implementation. Throws if a different provider
    /// was already selected on this service collection, so <c>UseTransport().RabbitMq()</c> followed by
    /// <c>UseTransport().Nats()</c> fails clearly instead of silently letting the second call win.
    /// </summary>
    public void SelectProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Transport provider name must not be empty.", nameof(providerName));

        var existing = Services
            .LastOrDefault(sd => sd.ServiceType == typeof(LyciaTransportProviderMarker))
            ?.ImplementationInstance as LyciaTransportProviderMarker;

        if (existing != null && !string.Equals(existing.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Lycia transport is already configured as '{existing.ProviderName}'. " +
                $"Cannot also select '{providerName}'. A Lycia application may register only one transport provider " +
                "(remove one of the UseTransport() calls, or explicitly override with LyciaBuilder.UseEventBus<T>()).");
        }

        Services.RemoveAll(typeof(LyciaTransportProviderMarker));
        Services.AddSingleton(new LyciaTransportProviderMarker(providerName));
    }

    /// <summary>Uses the deterministic in-process transport, suitable for tests and single-process samples.</summary>
    public LyciaTransportBuilder InMemory()
    {
        SelectProvider("InMemory");
        LyciaRegistrationExtensions.RegisterInMemoryEventBus(Services, Configuration["ApplicationId"]);
        return this;
    }
}

/// <summary>Tracks which transport provider has been selected on a service collection, for duplicate detection.</summary>
internal sealed class LyciaTransportProviderMarker
{
    public string ProviderName { get; }
    public LyciaTransportProviderMarker(string providerName) => ProviderName = providerName;
}
