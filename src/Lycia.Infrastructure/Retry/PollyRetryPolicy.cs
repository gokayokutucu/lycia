using Lycia.Exceptions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using IamRetryPolicy = Lycia.Infrastructure.Retry.IRetryPolicy;

namespace Lycia.Infrastructure.Retry;

// TODO: Take a look at LyciaRetryOptions and see if we can integrate it better with Polly's capabilities for net standard2.0
public class PollyRetryPolicy : IamRetryPolicy
{
    private readonly ResiliencePipeline _pipeline;
    public event Action<RetryContext>? OnRetry;

    public PollyRetryPolicy(IOptions<RetryStrategyOptions>? options)
    {
        var src = options?.Value;
        
        var opts = new RetryStrategyOptions
        {
            MaxRetryAttempts = src?.MaxRetryAttempts is > 0 ? src.MaxRetryAttempts : 3,
            BackoffType      = src?.BackoffType ?? DelayBackoffType.Exponential,
            Delay            = src?.Delay ?? TimeSpan.FromSeconds(1),
            MaxDelay         = src?.MaxDelay,                 
            UseJitter        = src?.UseJitter ?? true,
            ShouldHandle     = src?.ShouldHandle
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

    public bool ShouldRetry(Exception? exception, int currentRetryCount)
    {
        return exception is TransientSagaException or TimeoutException && currentRetryCount < 3;
    }

    public TimeSpan GetDelay(Exception? exception, int currentRetryCount)
    {
        return exception switch
        {
            TimeoutException => TimeSpan.FromSeconds(1),
            TransientSagaException => TimeSpan.FromSeconds(3),
            _ => TimeSpan.FromSeconds(Math.Pow(2, currentRetryCount))
        };
    }

    public ValueTask ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
        => _pipeline.ExecuteAsync(static (act, _) => new ValueTask(act()), action, cancellationToken);
}