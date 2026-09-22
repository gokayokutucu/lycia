// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Extensions.Reliability;

/// <summary>
/// Default <see cref="ILyciaReliabilityDiagnostics"/>: reads <see cref="IPersistenceTopology"/> for
/// the resolved boundary/topology and checks which optional stores are actually registered in the
/// current scope, rather than tracking a second, independently-maintained copy of that state.
/// </summary>
public sealed class LyciaReliabilityDiagnostics(
    IServiceProvider serviceProvider) : ILyciaReliabilityDiagnostics
{
    /// <inheritdoc />
    public LyciaReliabilitySnapshot GetSnapshot()
    {
        // IPersistenceTopology is only registered once an application calls UsePersistence(); resolve it
        // optionally so this diagnostics service itself never becomes a hard dependency on that path.
        var current = serviceProvider.GetService<IPersistenceTopology>()?.Current;
        var mode = current?.Mode ?? PersistenceMode.Standard;
        // Outside Split Store, name the registered SagaStore's provider directly (provider name only -
        // never PersistenceStoreDescriptor.ConnectionIdentity, which carries the host/database and is not
        // safe for this secret-free snapshot). In Split Store, CanonicalStore already names it, since the
        // canonical provider always owns the SagaStore (enforced by PersistenceTopologyConfiguration).
        var sagaStoreProvider = mode == PersistenceMode.Standard
            ? current?.Stores.FirstOrDefault(s => s.Capability == PersistenceCapabilityKind.SagaStore)?.ProviderName
            : null;
        return new LyciaReliabilitySnapshot
        {
            Mode = mode,
            SagaStoreProvider = sagaStoreProvider,
            CanonicalStore = current?.CanonicalStore,
            OperationalStore = current?.OperationalStore,
            ResolvedStrategy = current?.ResolvedStrategy ?? PersistenceExecutionStrategy.Independent,
            ReconciliationEnabled = current?.ReconciliationEnabled ?? false,
            JournalEnabled = IsRegistered<ISagaJournalStore>(),
            JournalRebuildAvailable = IsRegistered<ISagaRebuildService>(),
            InboxEnabled = IsRegistered<IInboxStore>(),
            OutboxEnabled = IsRegistered<IOutboxStore>()
        };
    }

    // Some store factories (for example the Redis Inbox/Outbox registrations) eagerly open a real
    // connection the first time the type is resolved. GetSnapshot() must stay a pure configuration read -
    // never a probe of the infrastructure it describes - so registration presence is checked through
    // IServiceProviderIsService, which inspects the DI registration table without invoking any factory
    // delegate or constructing an instance. The default Microsoft.Extensions.DependencyInjection container
    // (used by AddLycia's own BuildServiceProvider() and by the ASP.NET Core/generic host) always provides
    // one; GetService<T>() is only a fallback for a container that genuinely does not.
    private bool IsRegistered<T>() where T : class =>
        serviceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(T))
        ?? serviceProvider.GetService<T>() != null;
}
