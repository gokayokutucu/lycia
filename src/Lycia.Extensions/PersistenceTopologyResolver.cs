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

    public void SetStore(PersistenceStoreDescriptor descriptor) => _stores[descriptor.Capability] = descriptor;

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

        return new PersistenceTopology(Policy, strategy, stores, reason);
    }

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
