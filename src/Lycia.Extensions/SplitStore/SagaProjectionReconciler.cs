// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Lycia.Extensions.SplitStore;

internal sealed class SagaProjectionReconciler(
    IReconciliationStore reconciliationStore,
    IOperationalSagaProjectionStore operationalStore,
    IOptions<ReconciliationWorkerOptions> options,
    ILogger<SagaProjectionReconciler> logger) : ISagaProjectionReconciler
{
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private readonly Random _random = new();

    public async Task<ReconciliationRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        Validate(value);
        var intents = await reconciliationStore.ClaimAsync(_workerId, value.BatchSize, value.MaxAttempts,
            value.ClaimTimeout, cancellationToken).ConfigureAwait(false);
        var result = new ReconciliationRunResult { Claimed = intents.Count };

        foreach (var intent in intents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogDebug("Claimed saga projection {TransitionId} for saga {SagaId} version {SagaVersion}",
                intent.TransitionId, intent.SagaId, intent.TargetVersion);
            try
            {
                var outcome = await operationalStore.ApplyAsync(intent, cancellationToken).ConfigureAwait(false);
                switch (outcome)
                {
                    case ProjectionApplyOutcome.Applied:
                    case ProjectionApplyOutcome.AlreadyApplied:
                        await reconciliationStore.MarkCompletedAsync(intent.TransitionId, ReconciliationStatus.Applied,
                            cancellationToken).ConfigureAwait(false);
                        result.Applied++;
                        logger.LogInformation("Applied Redis saga projection {SagaId} version {SagaVersion}",
                            intent.SagaId, intent.TargetVersion);
                        break;
                    case ProjectionApplyOutcome.Superseded:
                        await reconciliationStore.MarkCompletedAsync(intent.TransitionId,
                            ReconciliationStatus.Superseded, cancellationToken).ConfigureAwait(false);
                        result.Superseded++;
                        logger.LogInformation("Superseded stale saga projection {SagaId} version {SagaVersion}",
                            intent.SagaId, intent.TargetVersion);
                        break;
                    case ProjectionApplyOutcome.VersionConflict:
                        await reconciliationStore.MarkFailedAsync(intent.TransitionId, "VersionConflict",
                            cancellationToken).ConfigureAwait(false);
                        result.Failed++;
                        logger.LogError("Saga projection version conflict for {SagaId} version {SagaVersion}",
                            intent.SagaId, intent.TargetVersion);
                        break;
                }
            }
            catch (JsonException exception)
            {
                await reconciliationStore.MarkFailedAsync(intent.TransitionId, "MalformedPayload", cancellationToken)
                    .ConfigureAwait(false);
                result.Failed++;
                logger.LogError(exception, "Malformed saga projection {TransitionId}", intent.TransitionId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (intent.AttemptCount >= value.MaxAttempts)
                {
                    await reconciliationStore.MarkFailedAsync(intent.TransitionId, "AttemptsExhausted",
                        cancellationToken).ConfigureAwait(false);
                    result.Failed++;
                    logger.LogError(exception, "Saga projection {TransitionId} exhausted retries", intent.TransitionId);
                }
                else
                {
                    var next = DateTime.UtcNow + RetryDelay(value, intent.AttemptCount);
                    await reconciliationStore.MarkRetryAsync(intent.TransitionId, next,
                        exception.GetType().Name, cancellationToken).ConfigureAwait(false);
                    result.Retried++;
                    logger.LogWarning(exception, "Saga projection {TransitionId} will retry at {NextAttemptAtUtc}",
                        intent.TransitionId, next);
                }
            }
        }

        return result;
    }

    public Task<bool> RestoreLatestAsync(Guid sagaId, CancellationToken cancellationToken = default) =>
        reconciliationStore.QueueLatestAsync(sagaId, cancellationToken);

    private TimeSpan RetryDelay(ReconciliationWorkerOptions value, int attempt)
    {
        var exponent = Math.Min(Math.Max(0, attempt - 1), 20);
        var milliseconds = Math.Min(value.MaxRetryBackoff.TotalMilliseconds,
            value.RetryBackoff.TotalMilliseconds * Math.Pow(2, exponent));
        var jitter = value.MaxJitter <= TimeSpan.Zero ? 0 : _random.NextDouble() * value.MaxJitter.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(milliseconds + jitter);
    }

    internal static void Validate(ReconciliationWorkerOptions value)
    {
        if (value.BatchSize <= 0 || value.MaxAttempts <= 0)
            throw new InvalidOperationException("Reconciliation BatchSize and MaxAttempts must be positive.");
        if (value.PollInterval <= TimeSpan.Zero || value.ClaimTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Reconciliation PollInterval and ClaimTimeout must be positive.");
        if (value.RetryBackoff < TimeSpan.Zero || value.MaxRetryBackoff < value.RetryBackoff || value.MaxJitter < TimeSpan.Zero)
            throw new InvalidOperationException("Reconciliation backoff and jitter values are invalid.");
    }
}
