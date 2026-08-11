// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lycia.Outbox;

/// <summary>Continuously dispatches durable outgoing messages with bounded attempts and backoff.</summary>
public sealed class OutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxWorkerOptions> options,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    private readonly Random _random = new();

    /// <summary>Runs one deterministic claim-and-dispatch pass.</summary>
    public async Task<OutboxDispatchResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        Validate(value);
        using var scope = scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
        return await dispatcher.DispatchPendingBatchAsync(value.BatchSize, cancellationToken, value.MaxAttempts,
                value.RecoveryTimeout)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var value = options.Value;
        Validate(value);
        if (!value.Enabled) return;

        var consecutiveUnconfirmedPasses = 0;
        logger.LogInformation("Lycia OutboxWorker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var result = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (result.ConfirmationUnknown > 0 || result.Failed > 0)
                {
                    consecutiveUnconfirmedPasses++;
                    delay = RetryDelay(value, consecutiveUnconfirmedPasses);
                }
                else
                {
                    consecutiveUnconfirmedPasses = 0;
                    delay = result.Claimed == 0 ? value.PollInterval : TimeSpan.Zero;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                consecutiveUnconfirmedPasses++;
                delay = RetryDelay(value, consecutiveUnconfirmedPasses);
                logger.LogError(exception, "OutboxWorker dispatch pass failed");
            }

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
        logger.LogInformation("Lycia OutboxWorker stopped");
    }

    private TimeSpan RetryDelay(OutboxWorkerOptions value, int attempt)
    {
        var exponent = Math.Min(attempt - 1, 20);
        var milliseconds = Math.Min(value.MaxRetryBackoff.TotalMilliseconds,
            value.RetryBackoff.TotalMilliseconds * Math.Pow(2, exponent));
        var jitter = value.MaxJitter <= TimeSpan.Zero ? 0 : _random.NextDouble() * value.MaxJitter.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(milliseconds + jitter);
    }

    private static void Validate(OutboxWorkerOptions value)
    {
        if (value.BatchSize <= 0) throw new InvalidOperationException("Outbox worker BatchSize must be positive.");
        if (value.MaxAttempts <= 0) throw new InvalidOperationException("Outbox worker MaxAttempts must be positive.");
        if (value.RecoveryTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Outbox worker RecoveryTimeout must be positive.");
        if (value.PollInterval <= TimeSpan.Zero) throw new InvalidOperationException("Outbox worker PollInterval must be positive.");
        if (value.RetryBackoff < TimeSpan.Zero || value.MaxRetryBackoff < value.RetryBackoff || value.MaxJitter < TimeSpan.Zero)
            throw new InvalidOperationException("Outbox worker backoff and jitter values are invalid.");
    }
}
