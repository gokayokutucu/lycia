// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Scheduling;

namespace Lycia.Scheduling;

/// <summary>Central conservative eligibility rules for dynamic scheduling-resource deletion.</summary>
public static class SchedulingVacuumEvaluator
{
    /// <summary>Returns an auditable decision without performing a destructive operation.</summary>
    public static VacuumDecision Evaluate(SchedulingResourceRecord resource, SchedulingResourceState state,
        DateTimeOffset nowUtc, SchedulingResourceVacuumOptions options, long activeSchedules)
    {
        if (resource == null) throw new ArgumentNullException(nameof(resource));
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (!resource.IsDynamic || resource.IsPredefined)
            return Reject(VacuumDecisionReason.PredefinedResource, "Predefined or non-dynamic resources are canonical topology.");
        if (resource.ManagementMode != SchedulingResourceManagementMode.DynamicScheduling || !state.OwnershipProven)
            return Reject(VacuumDecisionReason.UnknownOwnership, "Lycia dynamic-scheduling ownership is not proven.");
        if (state.IsProtected || resource.Lifecycle == SchedulingResourceLifecycle.Protected)
            return Reject(VacuumDecisionReason.Protected, "Resource is protected.");
        if (state.HasActiveManifestOwner)
            return Reject(VacuumDecisionReason.ActiveOwner, "An active deployment manifest owns the resource.");
        if (nowUtc - resource.CreatedAtUtc < options.MinimumResourceAge)
            return Reject(VacuumDecisionReason.NotOldEnough, "Minimum resource age has not passed.");
        if (nowUtc - resource.LastUsedAtUtc < options.DynamicResourceRetention)
            return Reject(VacuumDecisionReason.RecentlyUsed, "Dynamic resource retention has not passed.");
        if (activeSchedules > 0)
            return Reject(VacuumDecisionReason.ActiveSchedule, "A pending schedule references the resource.");
        if (state.MessageCount.GetValueOrDefault() > 0)
            return Reject(VacuumDecisionReason.HasMessages, "Broker resource contains messages.");
        if (state.ConsumerCount.GetValueOrDefault() > 0)
            return Reject(VacuumDecisionReason.HasConsumers, "Broker resource has active consumers.");
        return new VacuumDecision { Eligible = true, Reason = VacuumDecisionReason.Eligible, Detail = "All dynamic scheduling-resource safety checks passed." };
    }

    private static VacuumDecision Reject(VacuumDecisionReason reason, string detail) =>
        new() { Eligible = false, Reason = reason, Detail = detail };
}

/// <summary>Conservative orphan and quarantine rules for ordinary application topology.</summary>
public static class ApplicationTopologyOrphanEvaluator
{
    /// <summary>Advances lifecycle without ever treating inactivity alone as deletion proof.</summary>
    public static VacuumDecision Evaluate(SchedulingResourceRecord resource, SchedulingResourceState state,
        DateTimeOffset nowUtc, ApplicationTopologyVacuumOptions options, long activeSchedules)
    {
        if (resource == null) throw new ArgumentNullException(nameof(resource));
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (!state.OwnershipProven || resource.ManagementMode != SchedulingResourceManagementMode.LyciaManaged)
            return Reject(VacuumDecisionReason.UnknownOwnership, "Ordinary topology lacks Lycia registry provenance.");
        if (state.IsProtected || resource.ManagementMode == SchedulingResourceManagementMode.Protected)
            return Reject(VacuumDecisionReason.Protected, "Resource is protected.");
        if (state.HasActiveManifestOwner) return Reject(VacuumDecisionReason.ActiveOwner, "An active manifest owns the resource.");
        if (activeSchedules > 0) return Reject(VacuumDecisionReason.ActiveSchedule, "A pending schedule targets the resource.");
        if (state.MessageCount.GetValueOrDefault() > 0) return Reject(VacuumDecisionReason.HasMessages, "Resource contains messages.");
        if (state.ConsumerCount.GetValueOrDefault() > 0) return Reject(VacuumDecisionReason.HasConsumers, "Resource has consumers.");
        if (nowUtc - resource.LastUsedAtUtc < options.OrphanThreshold)
            return Reject(VacuumDecisionReason.RecentlyUsed, "Orphan threshold has not passed.");

        if (!resource.OrphanCandidateAtUtc.HasValue)
        {
            resource.OrphanCandidateAtUtc = nowUtc;
            resource.Lifecycle = SchedulingResourceLifecycle.OrphanCandidate;
            return Reject(VacuumDecisionReason.QuarantineIncomplete, "Resource entered orphan candidacy and must complete quarantine.");
        }
        if (nowUtc - resource.OrphanCandidateAtUtc.Value < options.QuarantinePeriod)
            return Reject(VacuumDecisionReason.QuarantineIncomplete, "Quarantine period has not completed.");
        resource.QuarantinedAtUtc ??= nowUtc;
        resource.Lifecycle = SchedulingResourceLifecycle.EligibleForDeletion;
        if (options.Mode != VacuumMode.Automatic || !options.AllowDestructiveApplicationTopologyCleanup)
            return Reject(VacuumDecisionReason.PolicyPreventsDeletion, "Application topology is ReportOnly, DryRun, or lacks destructive opt-in.");
        return new VacuumDecision { Eligible = true, Reason = VacuumDecisionReason.Eligible, Detail = "All ordinary topology deletion checks passed." };
    }

    private static VacuumDecision Reject(VacuumDecisionReason reason, string detail) =>
        new() { Eligible = false, Reason = reason, Detail = detail };
}
