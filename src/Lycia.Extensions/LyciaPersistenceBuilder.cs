// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Outbox;
using Lycia.Extensions.SplitStore;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
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
        EnsureTopologyServices();
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
        RegisterProviderMetadata(PersistenceCapabilityKind.SagaStore, providerName, null, false);
    }

    /// <summary>
    /// Marks <paramref name="providerName"/> as the selected Inbox provider. Throws if a different
    /// Inbox provider was already selected, so at most one Inbox implementation is ever active.
    /// </summary>
    public void SelectInboxProvider(string providerName) =>
        SelectCapabilityProvider<LyciaInboxProviderMarker>("Inbox", providerName, n => new LyciaInboxProviderMarker(n),
            PersistenceCapabilityKind.Inbox);

    /// <summary>
    /// Marks <paramref name="providerName"/> as the selected Outbox provider. Throws if a different
    /// Outbox provider was already selected, so at most one Outbox implementation is ever active.
    /// </summary>
    public void SelectOutboxProvider(string providerName) =>
        SelectCapabilityProvider<LyciaOutboxProviderMarker>("Outbox", providerName, n => new LyciaOutboxProviderMarker(n),
            PersistenceCapabilityKind.Outbox);

    private void SelectCapabilityProvider<TMarker>(string capabilityName, string providerName,
        Func<string, TMarker> createMarker, PersistenceCapabilityKind capability)
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
        RegisterProviderMetadata(capability, providerName, null, false);
    }

    /// <summary>
    /// Contributes safe provider metadata used to resolve the service-local persistence boundary.
    /// Provider packages should call this after selecting their store implementation.
    /// </summary>
    public void RegisterProviderMetadata(PersistenceCapabilityKind capability, string providerName,
        string? connectionIdentity, bool supportsRelationalLocalTransaction)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Persistence provider name must not be empty.", nameof(providerName));

        GetTopologyConfiguration().SetStore(new PersistenceStoreDescriptor(
            capability, providerName, connectionIdentity, supportsRelationalLocalTransaction));
    }

    /// <summary>Marks a relational SagaStore as canonical for an explicitly selected Split Store.</summary>
    public void SelectSplitStoreCanonicalProvider(string providerName, string connectionIdentity)
    {
        GetTopologyConfiguration().SetSplitStoreCanonical(new PersistenceStoreDescriptor(
            PersistenceCapabilityKind.SagaStore, providerName, connectionIdentity, true));
    }

    /// <summary>Marks Redis as the rebuildable operational Saga projection provider.</summary>
    public void SelectSplitStoreOperationalProvider(string providerName) =>
        GetTopologyConfiguration().SetSplitStoreOperational(providerName);

    /// <summary>
    /// Enables explicit Split Store ownership: relational SagaStore state is canonical and Redis is an
    /// asynchronously reconciled, rebuildable operational projection. Independent canonical transactions
    /// are rejected because the reconciliation intent must commit with Inbox, SagaStore, and Outbox.
    /// </summary>
    public LyciaPersistenceBuilder UseSplitStore()
    {
        var configuration = GetTopologyConfiguration();
        configuration.EnableSplitStore();

        if (!Services.Any(x => x.ServiceType == typeof(IReconciliationStore)))
            throw new InvalidOperationException("Split Store requires a canonical relational reconciliation store.");
        if (!Services.Any(x => x.ServiceType == typeof(IOperationalSagaProjectionStore)))
            throw new InvalidOperationException("Split Store requires a Redis operational saga projection store.");

        var canonicalDescriptor = Services.LastOrDefault(x => x.ServiceType == typeof(ISagaStore))
            ?? throw new InvalidOperationException("Split Store requires a canonical relational SagaStore.");
        Services.Remove(canonicalDescriptor);
        Services.AddScoped<ISagaStore>(sp => new SplitStoreSagaStore(
            CreateService(sp, canonicalDescriptor), sp.GetRequiredService<IReconciliationStore>()));
        Services.TryAddScoped<ISagaProjectionReconciler, SagaProjectionReconciler>();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService,
            ReconciliationWorker>());
        return this;
    }

    /// <summary>Configures bounded retries, polling, and stale-claim recovery for Split Store reconciliation.</summary>
    public LyciaPersistenceBuilder WithReconciliationWorker(Action<ReconciliationWorkerOptions> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        Services.Configure(configure);
        return this;
    }

    private static ISagaStore CreateService(IServiceProvider serviceProvider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is ISagaStore instance) return instance;
        if (descriptor.ImplementationFactory != null)
            return (ISagaStore)descriptor.ImplementationFactory(serviceProvider);
        if (descriptor.ImplementationType != null)
            return (ISagaStore)ActivatorUtilities.GetServiceOrCreateInstance(serviceProvider, descriptor.ImplementationType);
        throw new InvalidOperationException("The canonical SagaStore registration cannot be activated.");
    }

    /// <summary>Requires all enabled Lycia persistence stores to share one service-local atomic boundary.</summary>
    public LyciaPersistenceBuilder RequireAtomicBoundary()
    {
        GetTopologyConfiguration().SetPolicy(PersistenceBoundaryPolicy.RequireAtomic);
        return this;
    }

    /// <summary>
    /// Forces independent store operations even when the enabled relational stores could share one transaction.
    /// This intentionally gives up the atomic Lycia persistence boundary.
    /// </summary>
    public LyciaPersistenceBuilder UseIndependentTransactions()
    {
        GetTopologyConfiguration().SetPolicy(PersistenceBoundaryPolicy.ForceIndependent);
        return this;
    }

    private void EnsureTopologyServices()
    {
        _ = GetTopologyConfiguration();
        Services.TryAddScoped<ILyciaPersistenceSessionAccessor, LyciaPersistenceSessionAccessor>();
        Services.TryAddSingleton<IPersistenceTopology, PersistenceTopologyProvider>();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService,
            PersistenceTopologyValidationHostedService>());
    }

    private PersistenceTopologyConfiguration GetTopologyConfiguration()
    {
        var existing = Services.LastOrDefault(x => x.ServiceType == typeof(PersistenceTopologyConfiguration))
            ?.ImplementationInstance as PersistenceTopologyConfiguration;
        if (existing != null) return existing;

        var configuration = new PersistenceTopologyConfiguration();
        Services.AddSingleton(configuration);
        return configuration;
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
        return ActivateOutboxPipeline();
    }

    /// <summary>Activates durable outgoing capture and the hosted dispatcher for a selected provider.</summary>
    public LyciaPersistenceBuilder ActivateOutboxPipeline()
    {
        Services.TryAddScoped<IOutboxDispatcher, OutboxDispatcher>();
        Services.RemoveAll(typeof(IOutgoingMessagePipeline));
        Services.AddScoped<IOutgoingMessagePipeline, OutboxOutgoingMessagePipeline>();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, OutboxWorker>());
        return this;
    }

    /// <summary>Configures the hosted Outbox worker registered with the selected Outbox provider.</summary>
    public LyciaPersistenceBuilder WithOutboxWorker(Action<OutboxWorkerOptions> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        Services.Configure(configure);
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
