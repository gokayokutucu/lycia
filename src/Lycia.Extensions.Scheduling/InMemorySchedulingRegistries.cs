// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Scheduling;

namespace Lycia.Scheduling;

/// <summary>Thread-safe resource registry for tests and single-process development.</summary>
public sealed class InMemorySchedulingResourceRegistry : ISchedulingResourceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SchedulingResourceRecord> _records = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task UpsertAsync(SchedulingResourceRecord resource, CancellationToken cancellationToken = default)
    {
        if (resource == null) throw new ArgumentNullException(nameof(resource));
        if (string.IsNullOrWhiteSpace(resource.ResourceId)) throw new ArgumentException("ResourceId is required.", nameof(resource));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_records.TryGetValue(resource.ResourceId, out var existing))
            {
                var refreshed = Clone(resource);
                refreshed.CreatedAtUtc = existing.CreatedAtUtc <= resource.CreatedAtUtc
                    ? existing.CreatedAtUtc
                    : resource.CreatedAtUtc;
                _records[resource.ResourceId] = refreshed;
            }
            else _records.Add(resource.ResourceId, Clone(resource));
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SchedulingResourceRecord?> GetAsync(string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_records.TryGetValue(resourceId, out var record) ? Clone(record) : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SchedulingResourceRecord>> ListCandidatesAsync(int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<SchedulingResourceRecord>>(_records.Values
                .Where(record => record.Lifecycle != SchedulingResourceLifecycle.Deleted)
                .OrderBy(record => record.LastUsedAtUtc)
                .ThenBy(record => record.ResourceId, StringComparer.Ordinal)
                .Take(maximumCount).Select(Clone).ToArray());
    }

    /// <inheritdoc />
    public Task UpdateAsync(SchedulingResourceRecord resource, CancellationToken cancellationToken = default)
    {
        if (resource == null) throw new ArgumentNullException(nameof(resource));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _records[resource.ResourceId] = Clone(resource);
        return Task.CompletedTask;
    }

    private static SchedulingResourceRecord Clone(SchedulingResourceRecord source) => new()
    {
        ResourceId = source.ResourceId,
        Transport = source.Transport,
        ResourceType = source.ResourceType,
        CanonicalName = source.CanonicalName,
        CanonicalApplicationKey = source.CanonicalApplicationKey,
        MessageType = source.MessageType,
        MessageKind = source.MessageKind,
        Destination = source.Destination,
        Delay = source.Delay,
        DelaySuffix = source.DelaySuffix,
        IsPredefined = source.IsPredefined,
        IsDynamic = source.IsDynamic,
        ManagementMode = source.ManagementMode,
        Lifecycle = source.Lifecycle,
        CreatedAtUtc = source.CreatedAtUtc,
        LastDeclaredAtUtc = source.LastDeclaredAtUtc,
        LastUsedAtUtc = source.LastUsedAtUtc,
        LastPublishAtUtc = source.LastPublishAtUtc,
        LastDeliveryAtUtc = source.LastDeliveryAtUtc,
        LastConsumerSeenAtUtc = source.LastConsumerSeenAtUtc,
        OrphanCandidateAtUtc = source.OrphanCandidateAtUtc,
        QuarantinedAtUtc = source.QuarantinedAtUtc,
        DeletedAtUtc = source.DeletedAtUtc,
        LastError = source.LastError,
        FrameworkVersion = source.FrameworkVersion,
        TopologyVersion = source.TopologyVersion
    };
}

/// <summary>Thread-safe replica-aware manifest registry for deterministic tests.</summary>
public sealed class InMemoryTopologyManifestRegistry : ITopologyManifestRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TopologyManifest> _manifests = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task HeartbeatAsync(TopologyManifest manifest, CancellationToken cancellationToken = default)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        cancellationToken.ThrowIfCancellationRequested();
        var key = manifest.CanonicalApplicationKey + ":" + manifest.DeploymentId + ":" + manifest.InstanceId;
        lock (_gate) _manifests[key] = Clone(manifest);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TopologyManifest>> GetActiveAsync(DateTimeOffset nowUtc, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        cancellationToken.ThrowIfCancellationRequested();
        var cutoff = nowUtc.ToUniversalTime().Subtract(timeout);
        lock (_gate)
            return Task.FromResult<IReadOnlyList<TopologyManifest>>(_manifests.Values
                .Where(manifest => manifest.LastHeartbeatAtUtc >= cutoff).Select(Clone).ToArray());
    }

    private static TopologyManifest Clone(TopologyManifest source) => new()
    {
        ApplicationId = source.ApplicationId,
        CanonicalApplicationKey = source.CanonicalApplicationKey,
        DeploymentId = source.DeploymentId,
        InstanceId = source.InstanceId,
        TopologyVersion = source.TopologyVersion,
        StartedAtUtc = source.StartedAtUtc,
        LastHeartbeatAtUtc = source.LastHeartbeatAtUtc,
        OwnedResources = new HashSet<string>(source.OwnedResources, StringComparer.Ordinal)
    };
}

/// <summary>Single-process lease manager that exercises the same fencing semantics as distributed providers.</summary>
public sealed class InMemoryVacuumLeaseManager : IVacuumLeaseManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Lease> _leases = new(StringComparer.Ordinal);
    private long _nextFence;

    /// <inheritdoc />
    public Task<long?> TryAcquireAsync(string scope, string owner, DateTimeOffset nowUtc, TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_leases.TryGetValue(scope, out var current) && current.UntilUtc > nowUtc && current.Owner != owner)
                return Task.FromResult<long?>(null);
            var token = ++_nextFence;
            _leases[scope] = new Lease(owner, nowUtc.Add(duration), token);
            return Task.FromResult<long?>(token);
        }
    }

    /// <inheritdoc />
    public Task<bool> IsCurrentAsync(string scope, string owner, long fencingToken, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_leases.TryGetValue(scope, out var lease) && lease.Owner == owner &&
                                   lease.FencingToken == fencingToken && lease.UntilUtc > nowUtc);
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string scope, string owner, long fencingToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            if (_leases.TryGetValue(scope, out var lease) && lease.Owner == owner && lease.FencingToken == fencingToken)
                _leases.Remove(scope);
        return Task.CompletedTask;
    }

    private sealed class Lease(string owner, DateTimeOffset untilUtc, long fencingToken)
    {
        public string Owner { get; } = owner;
        public DateTimeOffset UntilUtc { get; } = untilUtc;
        public long FencingToken { get; } = fencingToken;
    }
}
