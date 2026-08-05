// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lycia.Scheduling;

internal static class SchedulingMetrics
{
    internal const string MeterName = "Lycia.Scheduling";
    private static readonly Meter Meter = new(MeterName);
    internal static readonly ActivitySource ActivitySource = new(MeterName);
    internal static readonly Counter<long> Requests = Meter.CreateCounter<long>("lycia.scheduling.requests");
    internal static readonly Counter<long> Claims = Meter.CreateCounter<long>("lycia.scheduling.claims");
    internal static readonly Counter<long> Dispatches = Meter.CreateCounter<long>("lycia.scheduling.dispatches");
    internal static readonly Counter<long> Failures = Meter.CreateCounter<long>("lycia.scheduling.failures");
    internal static readonly Counter<long> ResourcesCreated = Meter.CreateCounter<long>("lycia.scheduling.resources.created");
    internal static readonly Counter<long> VacuumDecisions = Meter.CreateCounter<long>("lycia.scheduling.vacuum.decisions");
    internal static readonly Histogram<double> DispatchLateness =
        Meter.CreateHistogram<double>("lycia.scheduling.dispatch.lateness", "ms");
}
