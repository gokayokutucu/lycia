// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Persistence;

/// <summary>Controls how Lycia resolves the service-local persistence transaction boundary.</summary>
public enum PersistenceBoundaryPolicy
{
    /// <summary>Uses a local atomic transaction when every enabled store can share one; otherwise uses independent operations.</summary>
    Auto,
    /// <summary>Requires every enabled store to share one local atomic transaction.</summary>
    RequireAtomic,
    /// <summary>Uses independent store operations even when a local atomic transaction is available.</summary>
    ForceIndependent
}

/// <summary>Describes the execution strategy selected for the configured Lycia persistence stores.</summary>
public enum PersistenceExecutionStrategy
{
    /// <summary>Stores execute independently and do not share one commit boundary.</summary>
    Independent,
    /// <summary>Enabled relational stores share one service-local database transaction.</summary>
    LocalAtomic
}

/// <summary>Identifies a Lycia persistence capability participating in handler processing.</summary>
public enum PersistenceCapabilityKind
{
    /// <summary>Saga state and step persistence.</summary>
    SagaStore,
    /// <summary>Incoming-message idempotency persistence.</summary>
    Inbox,
    /// <summary>Outgoing-message intent persistence.</summary>
    Outbox,
    /// <summary>Durable intent used to reconcile a rebuildable operational projection.</summary>
    Reconciliation,
    /// <summary>Canonical immutable saga transition history used for deterministic replay/rebuild.</summary>
    Journal
}

/// <summary>Identifies whether saga state uses one normal store or a canonical/operational split.</summary>
public enum PersistenceMode
{
    /// <summary>One selected SagaStore owns state.</summary>
    Standard,
    /// <summary>A relational store is canonical and Redis is an asynchronously reconciled projection.</summary>
    SplitStore
}

/// <summary>Safe, secret-free metadata describing an enabled persistence store.</summary>
public sealed class PersistenceStoreDescriptor
{
    /// <summary>Creates a persistence-store descriptor.</summary>
    public PersistenceStoreDescriptor(PersistenceCapabilityKind capability, string providerName,
        string? connectionIdentity, bool supportsRelationalLocalTransaction)
    {
        Capability = capability;
        ProviderName = providerName;
        ConnectionIdentity = connectionIdentity;
        SupportsRelationalLocalTransaction = supportsRelationalLocalTransaction;
    }

    /// <summary>The store capability.</summary>
    public PersistenceCapabilityKind Capability { get; }

    /// <summary>The provider name.</summary>
    public string ProviderName { get; }

    /// <summary>The safe normalized database identity, or <c>null</c> for non-relational stores.</summary>
    public string? ConnectionIdentity { get; }

    /// <summary>Whether the store can enlist in Lycia's shared relational transaction.</summary>
    public bool SupportsRelationalLocalTransaction { get; }
}

/// <summary>The normalized, resolved Lycia persistence topology for one service.</summary>
public sealed class PersistenceTopology
{
    /// <summary>Creates a resolved topology.</summary>
    public PersistenceTopology(
        PersistenceBoundaryPolicy boundaryPolicy,
        PersistenceExecutionStrategy resolvedStrategy,
        IReadOnlyList<PersistenceStoreDescriptor> stores,
        string reason,
        PersistenceMode mode = PersistenceMode.Standard,
        string? canonicalStore = null,
        string? operationalStore = null,
        bool reconciliationEnabled = false)
    {
        BoundaryPolicy = boundaryPolicy;
        ResolvedStrategy = resolvedStrategy;
        Stores = stores;
        Reason = reason;
        Mode = mode;
        CanonicalStore = canonicalStore;
        OperationalStore = operationalStore;
        ReconciliationEnabled = reconciliationEnabled;
    }

    /// <summary>The configured boundary policy.</summary>
    public PersistenceBoundaryPolicy BoundaryPolicy { get; }

    /// <summary>The strategy selected from the policy and normalized store topology.</summary>
    public PersistenceExecutionStrategy ResolvedStrategy { get; }

    /// <summary>All enabled Lycia persistence stores.</summary>
    public IReadOnlyList<PersistenceStoreDescriptor> Stores { get; }

    /// <summary>A secret-free explanation of the selected strategy.</summary>
    public string Reason { get; }

    /// <summary>The selected persistence ownership model.</summary>
    public PersistenceMode Mode { get; }

    /// <summary>The secret-free canonical provider name for Split Store, when enabled.</summary>
    public string? CanonicalStore { get; }

    /// <summary>The secret-free operational provider name for Split Store, when enabled.</summary>
    public string? OperationalStore { get; }

    /// <summary>Whether durable operational projection reconciliation is enabled.</summary>
    public bool ReconciliationEnabled { get; }

    /// <summary>Whether the enabled stores share one local relational transaction boundary.</summary>
    public bool AtomicBoundaryAvailable => ResolvedStrategy == PersistenceExecutionStrategy.LocalAtomic;
}

/// <summary>Provides the resolved persistence topology to runtime processing infrastructure.</summary>
public interface IPersistenceTopology
{
    /// <summary>Gets the normalized, resolved topology.</summary>
    PersistenceTopology Current { get; }
}
