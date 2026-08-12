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
        return new LyciaReliabilitySnapshot
        {
            Mode = current?.Mode ?? PersistenceMode.Standard,
            CanonicalStore = current?.CanonicalStore,
            OperationalStore = current?.OperationalStore,
            ResolvedStrategy = current?.ResolvedStrategy ?? PersistenceExecutionStrategy.Independent,
            ReconciliationEnabled = current?.ReconciliationEnabled ?? false,
            JournalEnabled = serviceProvider.GetService<ISagaJournalStore>() != null,
            JournalRebuildAvailable = serviceProvider.GetService<ISagaRebuildService>() != null,
            InboxEnabled = serviceProvider.GetService<IInboxStore>() != null,
            OutboxEnabled = serviceProvider.GetService<IOutboxStore>() != null
        };
    }
}
