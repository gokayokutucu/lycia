// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

namespace Lycia.Saga.Abstractions.Scheduling;

/// <summary>Management classification for a broker or persistence resource.</summary>
public enum SchedulingResourceManagementMode
{
    /// <summary>Ordinary resource created and managed by Lycia.</summary>
    LyciaManaged,
    /// <summary>Resource managed outside Lycia.</summary>
    ExternallyManaged,
    /// <summary>Resource excluded from automated deletion.</summary>
    Protected,
    /// <summary>Short-lived resource with explicit lifecycle rules.</summary>
    Ephemeral,
    /// <summary>Lycia-owned dynamic scheduling resource.</summary>
    DynamicScheduling
}

/// <summary>Ownership and quarantine lifecycle for managed resources.</summary>
public enum SchedulingResourceLifecycle
{
    /// <summary>Resource is currently required.</summary>
    Active,
    /// <summary>Strong evidence suggests no active owner.</summary>
    OrphanCandidate,
    /// <summary>Candidate has remained unowned for its quarantine period.</summary>
    Quarantined,
    /// <summary>Every configured safety condition permits conditional deletion.</summary>
    EligibleForDeletion,
    /// <summary>Resource was deleted conditionally.</summary>
    Deleted,
    /// <summary>Resource cannot be automatically deleted.</summary>
    Protected,
    /// <summary>Resource is outside the active policy scope.</summary>
    Ignored
}

/// <summary>Durable provenance and activity record for a Lycia-created resource.</summary>
public sealed class SchedulingResourceRecord
{
    /// <summary>Stable registry identifier.</summary>
    public string ResourceId { get; set; } = string.Empty;
    /// <summary>Transport name.</summary>
    public string Transport { get; set; } = string.Empty;
    /// <summary>Broker resource type such as queue, topic, stream, or consumer.</summary>
    public string ResourceType { get; set; } = string.Empty;
    /// <summary>Exact canonical broker name.</summary>
    public string CanonicalName { get; set; } = string.Empty;
    /// <summary>Canonical logical application owner.</summary>
    public string CanonicalApplicationKey { get; set; } = string.Empty;
    /// <summary>Assembly-qualified message type when applicable.</summary>
    public string? MessageType { get; set; }
    /// <summary>Message semantic when applicable.</summary>
    public ScheduledMessageKind? MessageKind { get; set; }
    /// <summary>Canonical target destination.</summary>
    public string? Destination { get; set; }
    /// <summary>Delay represented by this resource.</summary>
    public TimeSpan? Delay { get; set; }
    /// <summary>Canonical delay suffix.</summary>
    public string? DelaySuffix { get; set; }
    /// <summary>True for predefined canonical scheduling topology.</summary>
    public bool IsPredefined { get; set; }
    /// <summary>True for on-demand scheduling topology.</summary>
    public bool IsDynamic { get; set; }
    /// <summary>Management and provenance classification.</summary>
    public SchedulingResourceManagementMode ManagementMode { get; set; }
    /// <summary>Current orphan/quarantine lifecycle.</summary>
    public SchedulingResourceLifecycle Lifecycle { get; set; }
    /// <summary>UTC creation instant.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
    /// <summary>UTC last declaration instant.</summary>
    public DateTimeOffset LastDeclaredAtUtc { get; set; }
    /// <summary>UTC last scheduling use.</summary>
    public DateTimeOffset LastUsedAtUtc { get; set; }
    /// <summary>UTC last publish into this resource.</summary>
    public DateTimeOffset? LastPublishAtUtc { get; set; }
    /// <summary>UTC last observed delivery from this resource.</summary>
    public DateTimeOffset? LastDeliveryAtUtc { get; set; }
    /// <summary>UTC last instant active consumers were observed.</summary>
    public DateTimeOffset? LastConsumerSeenAtUtc { get; set; }
    /// <summary>UTC orphan candidacy instant.</summary>
    public DateTimeOffset? OrphanCandidateAtUtc { get; set; }
    /// <summary>UTC quarantine instant.</summary>
    public DateTimeOffset? QuarantinedAtUtc { get; set; }
    /// <summary>UTC conditional deletion instant.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }
    /// <summary>Last inspection or deletion error.</summary>
    public string? LastError { get; set; }
    /// <summary>Lycia assembly version that registered the resource.</summary>
    public string FrameworkVersion { get; set; } = string.Empty;
    /// <summary>Application topology version associated with the resource.</summary>
    public string TopologyVersion { get; set; } = "1";
}

/// <summary>Current broker-side safety facts used by vacuum evaluation.</summary>
public sealed class SchedulingResourceState
{
    /// <summary>True when the exact resource exists.</summary>
    public bool Exists { get; set; }
    /// <summary>Current queued or retained message count when available.</summary>
    public long? MessageCount { get; set; }
    /// <summary>Current active consumer count when available.</summary>
    public long? ConsumerCount { get; set; }
    /// <summary>True when an active manifest owns the resource.</summary>
    public bool HasActiveManifestOwner { get; set; }
    /// <summary>True when the resource is protected by category or explicit policy.</summary>
    public bool IsProtected { get; set; }
    /// <summary>True when Lycia registry provenance is cryptographically or durably established.</summary>
    public bool OwnershipProven { get; set; }
}

/// <summary>Auditable vacuum decision reason.</summary>
public enum VacuumDecisionReason
{
    /// <summary>Every safety condition passed.</summary>
    Eligible,
    /// <summary>Resource is not old enough.</summary>
    NotOldEnough,
    /// <summary>Resource has recent activity.</summary>
    RecentlyUsed,
    /// <summary>An active deployment manifest owns the resource.</summary>
    ActiveOwner,
    /// <summary>Resource contains messages.</summary>
    HasMessages,
    /// <summary>Resource has consumers.</summary>
    HasConsumers,
    /// <summary>A pending schedule targets the resource.</summary>
    ActiveSchedule,
    /// <summary>Resource is protected.</summary>
    Protected,
    /// <summary>Lycia ownership is not proven.</summary>
    UnknownOwnership,
    /// <summary>Resource is predefined canonical topology.</summary>
    PredefinedResource,
    /// <summary>Quarantine has not completed.</summary>
    QuarantineIncomplete,
    /// <summary>Policy is report-only or dry-run.</summary>
    PolicyPreventsDeletion,
    /// <summary>Broker rejected conditional deletion because the resource became active.</summary>
    Reactivated,
    /// <summary>Required broker permission is unavailable.</summary>
    PermissionDenied
}

/// <summary>Structured vacuum decision suitable for logs, metrics, and deterministic tests.</summary>
public sealed class VacuumDecision
{
    /// <summary>True when all safety checks permit deletion.</summary>
    public bool Eligible { get; set; }
    /// <summary>Primary decision reason.</summary>
    public VacuumDecisionReason Reason { get; set; }
    /// <summary>Human-readable audit detail without message payloads.</summary>
    public string Detail { get; set; } = string.Empty;
}

/// <summary>Durable registry for Lycia-created broker resources.</summary>
public interface ISchedulingResourceRegistry
{
    /// <summary>Creates or refreshes an exact resource record.</summary>
    Task UpsertAsync(SchedulingResourceRecord resource, CancellationToken cancellationToken = default);
    /// <summary>Gets an exact resource by registry id.</summary>
    Task<SchedulingResourceRecord?> GetAsync(string resourceId, CancellationToken cancellationToken = default);
    /// <summary>Lists cleanup candidates in deterministic order.</summary>
    Task<IReadOnlyList<SchedulingResourceRecord>> ListCandidatesAsync(int maximumCount,
        CancellationToken cancellationToken = default);
    /// <summary>Persists lifecycle and audit changes.</summary>
    Task UpdateAsync(SchedulingResourceRecord resource, CancellationToken cancellationToken = default);
}

/// <summary>Transport-specific exact inspection and conditional deletion operations.</summary>
public interface ISchedulingResourceManager
{
    /// <summary>Gets the managed transport name.</summary>
    string TransportName { get; }
    /// <summary>Reads current broker-side activity for an exact registered resource.</summary>
    Task<SchedulingResourceState> InspectAsync(SchedulingResourceRecord resource,
        CancellationToken cancellationToken = default);
    /// <summary>Deletes only if the broker still considers the resource empty and unused.</summary>
    Task<bool> DeleteConditionallyAsync(SchedulingResourceRecord resource,
        CancellationToken cancellationToken = default);
}

/// <summary>Distributed lease used to serialize vacuum decisions per transport and scope.</summary>
public interface IVacuumLeaseManager
{
    /// <summary>Attempts to acquire a named lease and returns its fencing token.</summary>
    Task<long?> TryAcquireAsync(string scope, string owner, DateTimeOffset nowUtc, TimeSpan duration,
        CancellationToken cancellationToken = default);
    /// <summary>Returns true only while owner and fencing token remain current.</summary>
    Task<bool> IsCurrentAsync(string scope, string owner, long fencingToken, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Releases a current lease.</summary>
    Task ReleaseAsync(string scope, string owner, long fencingToken,
        CancellationToken cancellationToken = default);
}

/// <summary>Heartbeat manifest shared by replicas of one logical application.</summary>
public sealed class TopologyManifest
{
    /// <summary>Configured logical application identity.</summary>
    public string ApplicationId { get; set; } = string.Empty;
    /// <summary>Canonical identity shared by equivalent ApplicationId spellings.</summary>
    public string CanonicalApplicationKey { get; set; } = string.Empty;
    /// <summary>Deployment identifier shared by replicas in one rollout.</summary>
    public string DeploymentId { get; set; } = string.Empty;
    /// <summary>Physical runtime identifier used only for diagnostics.</summary>
    public string InstanceId { get; set; } = string.Empty;
    /// <summary>Topology schema version.</summary>
    public string TopologyVersion { get; set; } = "1";
    /// <summary>UTC startup instant.</summary>
    public DateTimeOffset StartedAtUtc { get; set; }
    /// <summary>UTC heartbeat instant.</summary>
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }
    /// <summary>Exact resources owned by the logical deployment.</summary>
    public HashSet<string> OwnedResources { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Durable topology manifest registry.</summary>
public interface ITopologyManifestRegistry
{
    /// <summary>Creates or refreshes one replica heartbeat.</summary>
    Task HeartbeatAsync(TopologyManifest manifest, CancellationToken cancellationToken = default);
    /// <summary>Lists manifests that remain active after timeout and grace.</summary>
    Task<IReadOnlyList<TopologyManifest>> GetActiveAsync(DateTimeOffset nowUtc, TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
