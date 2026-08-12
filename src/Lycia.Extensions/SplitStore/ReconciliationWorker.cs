// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lycia.Extensions.SplitStore;

internal sealed class ReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReconciliationWorkerOptions> options,
    ILogger<ReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var value = options.Value;
        SagaProjectionReconciler.Validate(value);
        if (!value.Enabled) return;

        logger.LogInformation("Lycia Split Store reconciliation worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<ISagaProjectionReconciler>();
                var result = await reconciler.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (result.Claimed == 0)
                    await Task.Delay(value.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Split Store reconciliation pass failed");
                await Task.Delay(value.RetryBackoff, stoppingToken).ConfigureAwait(false);
            }
        }
        logger.LogInformation("Lycia Split Store reconciliation worker stopped");
    }
}
