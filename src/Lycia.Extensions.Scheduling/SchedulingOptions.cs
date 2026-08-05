// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Scheduling;

namespace Lycia.Scheduling;

/// <summary>Configuration for durable and broker-native scheduling.</summary>
public sealed class SchedulingOptions
{
    /// <summary>Gets or sets whether scheduling workers are enabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets the largest accepted delay.</summary>
    public TimeSpan MaximumDelay { get; set; } = TimeSpan.FromDays(3660);
    /// <summary>Gets or sets whether arbitrary delays may use transport-created dynamic resources.</summary>
    public bool AllowDynamicDelays { get; set; }
    /// <summary>Gets or sets whether a supported native transport strategy is preferred over the durable worker.</summary>
    public bool PreferNativeTransportScheduling { get; set; } = true;
    /// <summary>Gets or sets how often each replica refreshes its topology manifest.</summary>
    public TimeSpan ManifestHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets how long a missing replica heartbeat remains active before grace expires.</summary>
    public TimeSpan ManifestHeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(2);
    /// <summary>Gets or sets the predefined buckets enabled for eager topology declaration.</summary>
    public ISet<ScheduleDelay> PredefinedDelays { get; set; } =
        new HashSet<ScheduleDelay>((ScheduleDelay[])Enum.GetValues(typeof(ScheduleDelay)));
    /// <summary>Gets worker settings.</summary>
    public SchedulerWorkerOptions Worker { get; } = new();
    /// <summary>Gets vacuum settings.</summary>
    public VacuumOptions Vacuum { get; } = new();
}

/// <summary>Durable SchedulerWorker settings.</summary>
public sealed class SchedulerWorkerOptions
{
    /// <summary>Gets or sets whether the hosted worker runs.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets the polling interval.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Gets or sets the maximum records claimed per poll.</summary>
    public int BatchSize { get; set; } = 100;
    /// <summary>Gets or sets the distributed claim lease duration.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets how often an active dispatch renews its lease.</summary>
    public TimeSpan LeaseRenewInterval { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets or sets the maximum dispatch attempts before terminal failure.</summary>
    public int MaxDispatchAttempts { get; set; } = 10;
    /// <summary>Gets or sets retry delay after a failed dispatch.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the graceful drain limit.</summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Scheduling-resource and application-topology vacuum settings.</summary>
public sealed class VacuumOptions
{
    /// <summary>Gets scheduling-resource cleanup settings.</summary>
    public SchedulingResourceVacuumOptions SchedulingResources { get; } = new();
    /// <summary>Gets ordinary application-topology detection settings.</summary>
    public ApplicationTopologyVacuumOptions ApplicationTopology { get; } = new();
}

/// <summary>Cleanup settings for Lycia-owned dynamic scheduling resources.</summary>
public sealed class SchedulingResourceVacuumOptions
{
    /// <summary>Gets or sets whether the scheduling-resource vacuum runs.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets the worker interval.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    /// <summary>Gets or sets required dynamic-resource idle time.</summary>
    public TimeSpan DynamicResourceRetention { get; set; } = TimeSpan.FromDays(7);
    /// <summary>Gets or sets minimum resource age.</summary>
    public TimeSpan MinimumResourceAge { get; set; } = TimeSpan.FromDays(1);
    /// <summary>Gets or sets the maximum records evaluated per pass.</summary>
    public int BatchSize { get; set; } = 100;
    /// <summary>Gets or sets dry-run mode.</summary>
    public bool DryRun { get; set; }
}

/// <summary>Inspection and conservative deletion settings for ordinary application topology.</summary>
public sealed class ApplicationTopologyVacuumOptions
{
    /// <summary>Gets or sets the application-topology mode. The production-safe default is ReportOnly.</summary>
    public VacuumMode Mode { get; set; } = VacuumMode.ReportOnly;
    /// <summary>Gets or sets inactivity required before orphan candidacy.</summary>
    public TimeSpan OrphanThreshold { get; set; } = TimeSpan.FromDays(30);
    /// <summary>Gets or sets the quarantine interval required before deletion eligibility.</summary>
    public TimeSpan QuarantinePeriod { get; set; } = TimeSpan.FromDays(14);
    /// <summary>Gets or sets the second explicit opt-in required for destructive application cleanup.</summary>
    public bool AllowDestructiveApplicationTopologyCleanup { get; set; }
}

/// <summary>Ordinary application-topology inspection and deletion modes.</summary>
public enum VacuumMode
{
    /// <summary>Do not inspect ordinary application topology.</summary>
    Disabled,
    /// <summary>Detect and report candidates without deletion.</summary>
    ReportOnly,
    /// <summary>Run all eligibility checks and audit what would be deleted.</summary>
    DryRun,
    /// <summary>Conditionally delete only proven Lycia-managed resources after explicit opt-in.</summary>
    Automatic
}
