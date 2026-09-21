// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.Extensions.Hosting;

namespace Lycia.Extensions;

internal sealed class PersistenceTopologyConfiguration
{
    private readonly Dictionary<PersistenceCapabilityKind, PersistenceStoreDescriptor> _stores = new();

    public PersistenceBoundaryPolicy Policy { get; private set; } = PersistenceBoundaryPolicy.Auto;
    public bool SplitStoreEnabled { get; private set; }
    public PersistenceStoreDescriptor? CanonicalStore { get; private set; }
    public string? OperationalStore { get; private set; }

    public void SetStore(PersistenceStoreDescriptor descriptor) => _stores[descriptor.Capability] = descriptor;

    public void SetSplitStoreCanonical(PersistenceStoreDescriptor descriptor)
    {
        if (!descriptor.SupportsRelationalLocalTransaction || string.IsNullOrWhiteSpace(descriptor.ConnectionIdentity))
            throw new InvalidOperationException("Split Store canonical persistence must be a relational provider.");
        if (CanonicalStore != null && !SameStore(CanonicalStore, descriptor))
            throw new InvalidOperationException("Multiple Split Store canonical providers were configured.");
        CanonicalStore = descriptor;
    }

    public void SetSplitStoreOperational(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Operational provider name must not be empty.", nameof(providerName));
        if (OperationalStore != null && !string.Equals(OperationalStore, providerName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Multiple Split Store operational providers were configured.");
        OperationalStore = providerName;
    }

    public void EnableSplitStore() => SplitStoreEnabled = true;

    public void SetPolicy(PersistenceBoundaryPolicy policy)
    {
        if (Policy != PersistenceBoundaryPolicy.Auto && Policy != policy)
        {
            throw new InvalidOperationException(
                "Conflicting persistence boundary policies were configured. " +
                $"Existing policy: {Policy}, Conflicting policy: {policy}.");
        }

        Policy = policy;
    }

    public PersistenceTopology Resolve()
    {
        var stores = _stores.Values.OrderBy(x => x.Capability).ToArray();
        if (SplitStoreEnabled)
        {
            if (CanonicalStore == null)
                throw new InvalidOperationException("Split Store requires a relational canonical SagaStore registration.");
            if (!string.Equals(OperationalStore, "Redis", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Split Store requires Redis as the operational saga projection store.");
            if (!_stores.TryGetValue(PersistenceCapabilityKind.Reconciliation, out var reconciliation) ||
                !SameStore(CanonicalStore, reconciliation))
                throw new InvalidOperationException("Split Store reconciliation intents must use the canonical relational database.");
            if (!_stores.TryGetValue(PersistenceCapabilityKind.SagaStore, out var sagaStore) ||
                !SameStore(CanonicalStore, sagaStore))
                throw new InvalidOperationException("Split Store canonical provider must own the configured SagaStore.");
            if (Policy == PersistenceBoundaryPolicy.ForceIndependent)
                throw new InvalidOperationException("Split Store cannot use independent canonical transactions. " +
                    "Inbox, canonical SagaStore, Outbox, and reconciliation intent must share one local transaction.");
        }
        var canShare = stores.Length > 0 && stores.All(x =>
            x.SupportsRelationalLocalTransaction && !string.IsNullOrWhiteSpace(x.ConnectionIdentity));

        if (canShare)
        {
            var first = stores[0];
            canShare = stores.All(x =>
                string.Equals(x.ProviderName, first.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ConnectionIdentity, first.ConnectionIdentity, StringComparison.OrdinalIgnoreCase));
        }

        if (Policy == PersistenceBoundaryPolicy.RequireAtomic && !canShare)
        {
            throw new InvalidOperationException(BuildRequiredAtomicFailure(stores));
        }

        var strategy = Policy == PersistenceBoundaryPolicy.ForceIndependent || !canShare
            ? PersistenceExecutionStrategy.Independent
            : PersistenceExecutionStrategy.LocalAtomic;

        var reason = strategy == PersistenceExecutionStrategy.LocalAtomic
            ? "All enabled Lycia stores share one compatible relational database identity."
            : Policy == PersistenceBoundaryPolicy.ForceIndependent
                ? "Independent transactions were explicitly requested."
                : "The enabled Lycia stores do not share one compatible relational transaction boundary.";

        return new PersistenceTopology(Policy, strategy, stores, reason,
            SplitStoreEnabled ? PersistenceMode.SplitStore : PersistenceMode.Standard,
            CanonicalStore?.ProviderName, OperationalStore, SplitStoreEnabled);
    }

    private static bool SameStore(PersistenceStoreDescriptor left, PersistenceStoreDescriptor right) =>
        string.Equals(left.ProviderName, right.ProviderName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ConnectionIdentity, right.ConnectionIdentity, StringComparison.OrdinalIgnoreCase);

    private static string BuildRequiredAtomicFailure(IReadOnlyList<PersistenceStoreDescriptor> stores)
    {
        var lines = new List<string>
        {
            "Atomic Lycia persistence was required, but the configured stores do not share a compatible transaction boundary."
        };

        foreach (var store in stores)
        {
            lines.Add($"{store.Capability}: Provider={store.ProviderName}, Database={store.ConnectionIdentity ?? "not relational"}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed class PersistenceTopologyProvider(PersistenceTopologyConfiguration configuration) : IPersistenceTopology
{
    public PersistenceTopology Current => configuration.Resolve();
}

internal sealed class PersistenceTopologyValidationHostedService(IPersistenceTopology topology) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = topology.Current;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
