// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Persistence;

namespace Lycia.Extensions.AspNetCore;

/// <summary>
/// The secret-free JSON contract served by <c>GET /diagnostics/lycia</c>. This is a deliberate, stable
/// presentation projection of <see cref="LyciaReliabilitySnapshot"/>: it never carries more information
/// than the snapshot already exposes, and it never carries connection strings, credentials, addresses,
/// message payloads, or any other runtime/operational data.
/// </summary>
public sealed class LyciaDiagnosticsResponse
{
    /// <summary>
    /// Always <c>"AtLeastOnce"</c>. Lycia never claims exactly-once delivery; this field states the
    /// delivery guarantee explicitly rather than leaving it implicit.
    /// </summary>
    public string DeliveryGuarantee { get; init; } = "AtLeastOnce";

    /// <summary>The resolved persistence topology: mode, provider(s) and transaction strategy.</summary>
    public LyciaDiagnosticsPersistenceInfo Persistence { get; init; } = new();

    /// <summary>Which optional reliability capabilities are active.</summary>
    public LyciaDiagnosticsCapabilities Capabilities { get; init; } = new();

    /// <summary>Projects a <see cref="LyciaReliabilitySnapshot"/> into the stable HTTP response contract.</summary>
    public static LyciaDiagnosticsResponse FromSnapshot(LyciaReliabilitySnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        return new LyciaDiagnosticsResponse
        {
            DeliveryGuarantee = snapshot.DeliveryGuarantee,
            Persistence = new LyciaDiagnosticsPersistenceInfo
            {
                Mode = snapshot.Mode.ToString(),
                Provider = snapshot.SagaStoreProvider,
                ResolvedStrategy = snapshot.ResolvedStrategy.ToString(),
                CanonicalStore = snapshot.CanonicalStore,
                OperationalStore = snapshot.OperationalStore
            },
            Capabilities = new LyciaDiagnosticsCapabilities
            {
                Inbox = snapshot.InboxEnabled,
                Outbox = snapshot.OutboxEnabled,
                Journal = snapshot.JournalEnabled,
                JournalRebuild = snapshot.JournalRebuildAvailable,
                Reconciliation = snapshot.ReconciliationEnabled
            }
        };
    }
}

/// <summary>The resolved persistence topology: mode, provider(s) and transaction strategy.</summary>
public sealed class LyciaDiagnosticsPersistenceInfo
{
    /// <summary>The persistence topology mode: <c>"Standard"</c> (one SagaStore) or <c>"SplitStore"</c>.</summary>
    public string Mode { get; init; } = nameof(PersistenceMode.Standard);

    /// <summary>
    /// The registered SagaStore provider name outside Split Store (for example <c>"Redis"</c>,
    /// <c>"PostgreSql"</c>, <c>"SqlServer"</c>, <c>"InMemory"</c>), or <c>null</c> in Split Store mode or
    /// when no SagaStore is registered. In Split Store, <see cref="CanonicalStore"/> names the provider.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// The resolved service-local transaction strategy: <c>"Independent"</c> or <c>"LocalAtomic"</c>.
    /// </summary>
    public string ResolvedStrategy { get; init; } = nameof(PersistenceExecutionStrategy.Independent);

    /// <summary>The Split Store canonical (relational) SagaStore provider name, or <c>null</c> outside Split Store.</summary>
    public string? CanonicalStore { get; init; }

    /// <summary>The Split Store operational projection provider name (Redis), or <c>null</c> outside Split Store.</summary>
    public string? OperationalStore { get; init; }
}

/// <summary>Which optional Lycia reliability capabilities are active for the current application.</summary>
public sealed class LyciaDiagnosticsCapabilities
{
    /// <summary>Whether an Inbox provider (duplicate-delivery suppression) is registered.</summary>
    public bool Inbox { get; init; }

    /// <summary>Whether an Outbox provider (durable outgoing intent) is registered.</summary>
    public bool Outbox { get; init; }

    /// <summary>Whether a canonical journal store is registered.</summary>
    public bool Journal { get; init; }

    /// <summary>Whether journal-based rebuild/verify (<c>ISagaRebuildService</c>) is available.</summary>
    public bool JournalRebuild { get; init; }

    /// <summary>Whether Split Store operational-projection reconciliation is active.</summary>
    public bool Reconciliation { get; init; }
}
