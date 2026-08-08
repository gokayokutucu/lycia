// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Outbox;
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

    /// <summary>
    /// Marks <paramref name="providerName"/> as the selected Inbox provider. Throws if a different
    /// Inbox provider was already selected, so at most one Inbox implementation is ever active.
    /// </summary>
    public void SelectInboxProvider(string providerName) =>
        SelectCapabilityProvider<LyciaInboxProviderMarker>("Inbox", providerName, n => new LyciaInboxProviderMarker(n));

    /// <summary>
    /// Marks <paramref name="providerName"/> as the selected Outbox provider. Throws if a different
    /// Outbox provider was already selected, so at most one Outbox implementation is ever active.
    /// </summary>
    public void SelectOutboxProvider(string providerName) =>
        SelectCapabilityProvider<LyciaOutboxProviderMarker>("Outbox", providerName, n => new LyciaOutboxProviderMarker(n));

    private void SelectCapabilityProvider<TMarker>(string capabilityName, string providerName, Func<string, TMarker> createMarker)
        where TMarker : class, ICapabilityProviderMarker
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException($"{capabilityName} provider name must not be empty.", nameof(providerName));

        var existing = Services
            .LastOrDefault(sd => sd.ServiceType == typeof(TMarker))
            ?.ImplementationInstance as TMarker;

        if (existing != null && !string.Equals(existing.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Multiple {capabilityName} providers were configured. " +
                $"Existing provider: {existing.ProviderName}, Conflicting provider: {providerName}. " +
                $"Exactly one {capabilityName} provider is allowed.");
        }

        Services.RemoveAll(typeof(TMarker));
        Services.AddSingleton(createMarker(providerName));
    }

    /// <summary>
    /// Registers a custom <see cref="IInboxStore"/> implementation and marks Inbox as enabled. Provider
    /// packages should expose a named <c>With...Inbox()</c> extension calling this, the same way
    /// <c>WithRedisSagaStore</c>/etc. wrap SagaStore registration.
    /// </summary>
    public LyciaPersistenceBuilder WithInbox<TInbox>() where TInbox : class, IInboxStore
    {
        SelectInboxProvider(typeof(TInbox).Name);
        Services.RemoveAll(typeof(IInboxStore));
        Services.AddSingleton<IInboxStore, TInbox>();
        return this;
    }

    /// <summary>
    /// Registers a custom <see cref="IOutboxStore"/> implementation and marks Outbox as enabled. Provider
    /// packages should expose a named <c>With...Outbox()</c> extension calling this, the same way
    /// <c>WithRedisSagaStore</c>/etc. wrap SagaStore registration.
    /// </summary>
    public LyciaPersistenceBuilder WithOutbox<TOutbox>() where TOutbox : class, IOutboxStore
    {
        SelectOutboxProvider(typeof(TOutbox).Name);
        Services.RemoveAll(typeof(IOutboxStore));
        Services.AddSingleton<IOutboxStore, TOutbox>();
        return this;
    }
}

internal interface ICapabilityProviderMarker
{
    string ProviderName { get; }
}

/// <summary>Tracks which SagaStore provider has been selected on a service collection, for duplicate detection.</summary>
internal sealed class LyciaSagaStoreProviderMarker
{
    public string ProviderName { get; }
    public LyciaSagaStoreProviderMarker(string providerName) => ProviderName = providerName;
}

/// <summary>Tracks which Inbox provider has been selected on a service collection, for duplicate detection.</summary>
internal sealed class LyciaInboxProviderMarker(string providerName) : ICapabilityProviderMarker
{
    public string ProviderName { get; } = providerName;
}

/// <summary>Tracks which Outbox provider has been selected on a service collection, for duplicate detection.</summary>
internal sealed class LyciaOutboxProviderMarker(string providerName) : ICapabilityProviderMarker
{
    public string ProviderName { get; } = providerName;
}
