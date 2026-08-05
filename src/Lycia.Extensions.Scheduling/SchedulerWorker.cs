// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lycia.Scheduling;

/// <summary>Durably claims and dispatches due schedules with lease and fencing protection.</summary>
public sealed class SchedulerWorker(
    IScheduleStore store,
    ISchedulingDispatcher dispatcher,
    ISchedulingClock clock,
    IOptions<SchedulingOptions> options,
    ILogger<SchedulerWorker> logger) : BackgroundService
{
    private readonly string _leaseOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private readonly CancellationTokenSource _dispatchCancellation = new();

    /// <summary>Runs one deterministic claim-and-dispatch pass, primarily for tests and operational probes.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        => await RunOnceAsync(cancellationToken, cancellationToken).ConfigureAwait(false);

    private async Task<int> RunOnceAsync(CancellationToken claimCancellationToken,
        CancellationToken dispatchCancellationToken)
    {
        var worker = options.Value.Worker;
        var claims = await store.ClaimDueAsync(clock.UtcNow, worker.BatchSize, _leaseOwner,
            worker.LeaseDuration, claimCancellationToken).ConfigureAwait(false);
        if (claims.Count > 0) SchedulingMetrics.Claims.Add(claims.Count);
        foreach (var claim in claims)
            await DispatchClaimAsync(claim, dispatchCancellationToken).ConfigureAwait(false);
        return claims.Count;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !options.Value.Worker.Enabled) return;
        logger.LogInformation("Lycia SchedulerWorker {LeaseOwner} started", _leaseOwner);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken, _dispatchCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "SchedulerWorker polling failed for owner {LeaseOwner}", _leaseOwner);
            }

            await Task.Delay(options.Value.Worker.PollInterval, stoppingToken).ConfigureAwait(false);
        }
        logger.LogInformation("Lycia SchedulerWorker {LeaseOwner} stopped", _leaseOwner);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var stop = base.StopAsync(cancellationToken);
        var drain = Task.Delay(options.Value.Worker.ShutdownDrainTimeout, cancellationToken);
        if (await Task.WhenAny(stop, drain).ConfigureAwait(false) != stop)
        {
            logger.LogWarning("SchedulerWorker drain timeout elapsed; cancelling active dispatches for {LeaseOwner}",
                _leaseOwner);
            _dispatchCancellation.Cancel();
        }
        await stop.ConfigureAwait(false);
    }

    private async Task DispatchClaimAsync(ScheduleClaim claim, CancellationToken cancellationToken)
    {
        var record = claim.Record;
        using var activity = SchedulingMetrics.ActivitySource.StartActivity("lycia.schedule.dispatch");
        activity?.SetTag("lycia.schedule_id", record.ScheduleId);
        activity?.SetTag("lycia.message_id", record.MessageId);
        activity?.SetTag("lycia.due_at", record.DueAtUtc);
        activity?.SetTag("lycia.scheduling_attempt", record.AttemptCount + 1);
        activity?.SetTag("lycia.scheduling_strategy", record.Strategy.ToString());
        SchedulingMetrics.DispatchLateness.Record(Math.Max(0, (clock.UtcNow - record.DueAtUtc).TotalMilliseconds),
            new KeyValuePair<string, object?>("message.kind", record.MessageKind.ToString()));
        var dispatching = await store.MarkDispatchingAsync(record.ScheduleId, claim.LeaseOwner,
            claim.FencingToken, cancellationToken).ConfigureAwait(false);
        if (!dispatching)
        {
            logger.LogWarning("Skipped stale scheduling claim {ScheduleId} with fencing token {FencingToken}",
                record.ScheduleId, claim.FencingToken);
            return;
        }

        try
        {
            using var renewalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var renewal = RenewLeaseAsync(claim, renewalCancellation.Token);
            try
            {
                await dispatcher.DispatchAsync(record, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                renewalCancellation.Cancel();
                await AwaitRenewalShutdownAsync(renewal).ConfigureAwait(false);
            }
            var completed = await store.CompleteAsync(record.ScheduleId, claim.LeaseOwner, claim.FencingToken,
                clock.UtcNow, cancellationToken).ConfigureAwait(false);
            if (!completed)
                logger.LogWarning("Dispatch completed but stale fencing token prevented completion of {ScheduleId}",
                    record.ScheduleId);
            else
            {
                SchedulingMetrics.Dispatches.Add(1,
                    new KeyValuePair<string, object?>("message.kind", record.MessageKind.ToString()),
                    new KeyValuePair<string, object?>("outcome", "completed"));
                logger.LogInformation(
                    "Dispatched schedule {ScheduleId} message {MessageId} kind {MessageKind} due {DueAtUtc} attempt {Attempt}",
                    record.ScheduleId, record.MessageId, record.MessageKind, record.DueAtUtc, record.AttemptCount + 1);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var attempt = record.AttemptCount + 1;
            var terminal = attempt >= options.Value.Worker.MaxDispatchAttempts;
            var retryAt = terminal ? (DateTimeOffset?)null : clock.UtcNow.Add(options.Value.Worker.RetryBackoff);
            await store.FailAsync(record.ScheduleId, claim.LeaseOwner, claim.FencingToken,
                exception.GetType().Name + ": " + exception.Message, retryAt, cancellationToken).ConfigureAwait(false);
            SchedulingMetrics.Failures.Add(1,
                new KeyValuePair<string, object?>("message.kind", record.MessageKind.ToString()),
                new KeyValuePair<string, object?>("terminal", terminal));
            logger.LogError(exception,
                "Scheduling dispatch failed for {ScheduleId} message {MessageId} attempt {Attempt}; terminal={Terminal}",
                record.ScheduleId, record.MessageId, attempt, terminal);
        }
    }

    private async Task RenewLeaseAsync(ScheduleClaim claim, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(options.Value.Worker.LeaseRenewInterval, cancellationToken).ConfigureAwait(false);
            var renewed = await store.RenewLeaseAsync(claim.Record.ScheduleId, claim.LeaseOwner,
                claim.FencingToken, clock.UtcNow.Add(options.Value.Worker.LeaseDuration), cancellationToken)
                .ConfigureAwait(false);
            if (renewed) continue;
            logger.LogWarning("Lease renewal was rejected for schedule {ScheduleId} with fencing token {FencingToken}",
                claim.Record.ScheduleId, claim.FencingToken);
            return;
        }
    }

    private static async Task AwaitRenewalShutdownAsync(Task renewal)
    {
        try { await renewal.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}
