// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence;

/// <summary>
/// A safe, secret-free snapshot of the active persistence/reliability topology, intended for
/// operational diagnostics (health endpoints, startup logs, future Lycia Doctor tooling). Never
/// includes connection strings, credentials, or payloads - only provider names, capability flags,
/// and the resolved transaction boundary.
/// </summary>
public sealed class LyciaReliabilitySnapshot
{
    /// <summary>The persistence topology mode (single-store or Split Store).</summary>
    public PersistenceMode Mode { get; set; }

    /// <summary>The canonical SagaStore provider name, or <c>null</c> outside Split Store.</summary>
    public string? CanonicalStore { get; set; }

    /// <summary>The operational projection provider name (Redis in Split Store), or <c>null</c> outside Split Store.</summary>
    public string? OperationalStore { get; set; }

    /// <summary>The resolved service-local transaction boundary for enabled stores.</summary>
    public PersistenceExecutionStrategy ResolvedStrategy { get; set; }

    /// <summary>Whether Split Store reconciliation is active.</summary>
    public bool ReconciliationEnabled { get; set; }

    /// <summary>Whether a canonical journal store is registered (mandatory once Split Store is enabled).</summary>
    public bool JournalEnabled { get; set; }

    /// <summary>Whether an <c>ISagaRebuildService</c> is registered, i.e. journal-based rebuild/verify is available.</summary>
    public bool JournalRebuildAvailable { get; set; }

    /// <summary>Whether an Inbox provider is registered.</summary>
    public bool InboxEnabled { get; set; }

    /// <summary>Whether an Outbox provider is registered.</summary>
    public bool OutboxEnabled { get; set; }

    /// <summary>
    /// Always <c>AtLeastOnce</c>. Lycia never claims exactly-once delivery; this field exists so
    /// diagnostic output states the delivery guarantee explicitly rather than leaving it implicit.
    /// </summary>
    public string DeliveryGuarantee { get; } = "AtLeastOnce";
}
