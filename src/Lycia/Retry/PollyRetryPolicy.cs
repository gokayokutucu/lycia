using Lycia.Saga.Exceptions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

#if NET8_0_OR_GREATER
namespace Lycia.Retry;

/// <summary>Implements Lycia retry semantics with a Polly resilience pipeline.</summary>
public class PollyRetryPolicy : IRetryPolicy
{
    private readonly ResiliencePipeline _pipeline;
    /// <inheritdoc />
    public event Action<RetryContext>? OnRetry;

    /// <summary>Creates a retry policy from optional strategy settings.</summary>
    public PollyRetryPolicy(IOptions<RetryStrategyOptions>? options)
    {
        var src = options?.Value;

        var opts = new RetryStrategyOptions
        {
            MaxRetryAttempts = src?.MaxRetryAttempts is > 0 ? src.MaxRetryAttempts : 3,
            BackoffType = src?.BackoffType ?? DelayBackoffType.Exponential,
            Delay = src?.Delay ?? TimeSpan.FromSeconds(1),
            MaxDelay = src?.MaxDelay,
            UseJitter = src?.UseJitter ?? true,
            ShouldHandle = src?.ShouldHandle
                               ?? new PredicateBuilder()
                                   .Handle<TransientSagaException>()
                                   .Handle<TimeoutException>()
        };

        var prevOnRetry = src?.OnRetry;
        opts.OnRetry = async args =>
        {
            if (prevOnRetry is not null)
                await prevOnRetry(args).ConfigureAwait(false);

            var ctx = new RetryContext(args.Outcome.Exception!, args.AttemptNumber, args.RetryDelay);
            OnRetry?.Invoke(ctx);
        };

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(opts)
            .Build();
    }

    /// <inheritdoc />
    public bool ShouldRetry(Exception? exception, int currentRetryCount)
    {
        return exception is TransientSagaException or TimeoutException && currentRetryCount < 3;
    }

    /// <inheritdoc />
    public TimeSpan GetDelay(Exception? exception, int currentRetryCount)
    {
        return exception switch
        {
            TimeoutException => TimeSpan.FromSeconds(1),
            TransientSagaException => TimeSpan.FromSeconds(3),
            _ => TimeSpan.FromSeconds(Math.Pow(2, currentRetryCount))
        };
    }

    /// <inheritdoc />
    public ValueTask ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
        => _pipeline.ExecuteAsync(static (act, _) => new ValueTask(act()), action, cancellationToken);
} 
#endif
