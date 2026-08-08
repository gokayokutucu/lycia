// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lycia.Extensions;

/// <summary>
/// Fluent persistence DSL reached via <see cref="LyciaBuilder.UsePersistence"/>. <c>Lycia.Extensions</c>
/// defines only this builder and its provider-selection guard; it takes no compile-time dependency on any
/// concrete persistence package. SagaStore/Inbox/Outbox provider packages (e.g. <c>Lycia.Persistence.InMemory</c>,
/// <c>Lycia.Persistence.Redis</c>, <c>Lycia.Persistence.SqlServer</c>, <c>Lycia.Persistence.PostgreSql</c>)
/// contribute their own <c>With...()</c> extension methods on this type, the same pattern
/// <see cref="LyciaTransportBuilder"/> uses for transport packages. Configuration values (connection strings,
/// timeouts, schema names) may still flow from <see cref="Configuration"/>/<c>IOptions</c>; provider selection
/// itself is always an explicit code-first call, never inferred from a configuration string.
/// </summary>
public sealed class LyciaPersistenceBuilder
{
    /// <summary>The underlying service collection persistence providers register into.</summary>
    public IServiceCollection Services { get; }

    /// <summary>The configuration Lycia was bootstrapped with.</summary>
    public IConfiguration Configuration { get; }

    internal LyciaPersistenceBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;
    }

    /// <summary>
    /// Marks <paramref name="providerName"/> as the selected SagaStore provider. SagaStore provider packages
    /// must call this before registering their <c>ISagaStore</c> implementation. Throws if a different
    /// SagaStore provider was already selected on this service collection, so e.g.
    /// <c>UsePersistence().WithRedisSagaStore(...)</c> followed by <c>.WithPostgreSqlSagaStore(...)</c> fails
    /// clearly instead of silently letting the last registration win.
    /// </summary>
    public void SelectProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("SagaStore provider name must not be empty.", nameof(providerName));

        var existing = Services
            .LastOrDefault(sd => sd.ServiceType == typeof(LyciaSagaStoreProviderMarker))
            ?.ImplementationInstance as LyciaSagaStoreProviderMarker;

        if (existing != null && !string.Equals(existing.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Multiple SagaStore providers were configured. " +
                $"Existing provider: {existing.ProviderName}, Conflicting provider: {providerName}. " +
                "Exactly one SagaStore provider is allowed.");
        }

        Services.RemoveAll(typeof(LyciaSagaStoreProviderMarker));
        Services.AddSingleton(new LyciaSagaStoreProviderMarker(providerName));
    }
}

/// <summary>Tracks which SagaStore provider has been selected on a service collection, for duplicate detection.</summary>
internal sealed class LyciaSagaStoreProviderMarker
{
    public string ProviderName { get; }
    public LyciaSagaStoreProviderMarker(string providerName) => ProviderName = providerName;
}
