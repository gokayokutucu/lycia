// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Compensating;
using Lycia.Saga.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lycia.Compensating;

/// <summary>
/// Recovery-only safety net for durable compensation-propagation edges (<c>CompensationPropagationIntent</c>).
/// It is never the mandatory happy-path executor: on the healthy path, <c>ThenBubbleUp(ct)</c>
/// (via <c>SagaCompensationCoordinator.CompensateParentAsync</c>) durably claims and immediately attempts
/// propagation in the same call, and this worker never sees that edge at all. This worker only resumes edges
/// left <c>Pending</c> (the immediate attempt never ran - e.g. the process crashed right after the durable
/// claim committed) or left with a stale <c>Claimed</c> lease (the immediate attempt started but never
/// finished - e.g. the process crashed mid-invocation, or the parent handler is simply slow). See
/// <c>DEVELOPERS.md</c>, "Coordinated compensation continuation", for the full state machine.
/// </summary>
public sealed class CompensationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CompensationWorkerOptions> options,
    ILogger<CompensationWorker> logger) : BackgroundService
{
    private readonly Random _random = new();
    private readonly string _ownerPrefix = $"worker:{Guid.NewGuid():N}";

    /// <summary>Runs one deterministic claim-and-resume pass.</summary>
    public async Task<CompensationRecoveryResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        Validate(value);

        using var scope = scopeFactory.CreateScope();
        var sagaStore = scope.ServiceProvider.GetRequiredService<ISagaStore>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        // SagaCompensationCoordinator.AttemptPropagationAsync is intentionally internal, not part of
        // ISagaCompensationCoordinator - it is a shared implementation detail between the immediate
        // in-request attempt and this worker, not a public coordination concept. This cast is safe: the
        // container only ever registers the concrete SagaCompensationCoordinator for that interface, and
        // this type lives in the same assembly, so the internal member is accessible.
        var coordinator = (SagaCompensationCoordinator)scope.ServiceProvider.GetRequiredService<ISagaCompensationCoordinator>();

        var owner = $"{_ownerPrefix}:{Guid.NewGuid():N}";
        var due = await sagaStore.ClaimDueCompensationPropagationsAsync(value.BatchSize, owner, value.RecoveryTimeout,
                value.RecoveryTimeout, value.MaxAttempts, cancellationToken)
            .ConfigureAwait(false);

        var result = new CompensationRecoveryResult { Claimed = due.Count };
        foreach (var intent in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await coordinator.AttemptPropagationAsync(intent.SagaId, intent.ChildMessageId, eventBus, sagaStore,
                    cancellationToken).ConfigureAwait(false);
                result.Succeeded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                result.Failed++;
                // The edge intentionally stays Claimed (see AttemptPropagationAsync remarks): it is
                // recovered again once this claim's lease (RecoveryTimeout) goes stale, bounded by
                // MaxAttempts, at which point the store itself moves it to the terminal Failed state.
                logger.LogWarning(exception,
                    "CompensationWorker propagation attempt failed [SagaId={SagaId}, ChildMessageId={ChildMessageId}]; " +
                    "the edge remains claimed and will be retried after its lease goes stale.",
                    intent.SagaId, intent.ChildMessageId);
            }
        }

        return result;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var value = options.Value;
        Validate(value);
        if (!value.Enabled) return;

        var consecutiveFailedPasses = 0;
        logger.LogInformation("Lycia CompensationWorker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var result = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (result.Failed > 0)
                {
                    consecutiveFailedPasses++;
                    delay = RetryDelay(value, consecutiveFailedPasses);
                }
                else
                {
                    consecutiveFailedPasses = 0;
                    delay = result.Claimed == 0 ? value.PollInterval : TimeSpan.Zero;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                consecutiveFailedPasses++;
                delay = RetryDelay(value, consecutiveFailedPasses);
                logger.LogError(exception, "CompensationWorker recovery pass failed");
            }

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
        logger.LogInformation("Lycia CompensationWorker stopped");
    }

    private TimeSpan RetryDelay(CompensationWorkerOptions value, int attempt)
    {
        var exponent = Math.Min(attempt - 1, 20);
        var milliseconds = Math.Min(value.MaxRetryBackoff.TotalMilliseconds,
            value.RetryBackoff.TotalMilliseconds * Math.Pow(2, exponent));
        var jitter = value.MaxJitter <= TimeSpan.Zero ? 0 : _random.NextDouble() * value.MaxJitter.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(milliseconds + jitter);
    }

    private static void Validate(CompensationWorkerOptions value)
    {
        if (value.BatchSize <= 0) throw new InvalidOperationException("Compensation worker BatchSize must be positive.");
        if (value.MaxAttempts <= 0) throw new InvalidOperationException("Compensation worker MaxAttempts must be positive.");
        if (value.RecoveryTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Compensation worker RecoveryTimeout must be positive.");
        if (value.PollInterval <= TimeSpan.Zero) throw new InvalidOperationException("Compensation worker PollInterval must be positive.");
        if (value.RetryBackoff < TimeSpan.Zero || value.MaxRetryBackoff < value.RetryBackoff || value.MaxJitter < TimeSpan.Zero)
            throw new InvalidOperationException("Compensation worker backoff and jitter values are invalid.");
    }
}
