using Lycia.Saga.Exceptions;
using Polly;
using Polly.Retry;
using Retry_IRetryPolicy = Lycia.Infrastructure.Retry.IRetryPolicy;

namespace Lycia.Infrastructure.Retry;

public class PollyRetryPolicy : Retry_IRetryPolicy
{
    private readonly AsyncRetryPolicy _policy;
    public event Action<RetryContext>? OnRetry;

    public PollyRetryPolicy()
    {
        _policy = Policy
            .Handle<LyciaSagaException>(ex => ex is TransientSagaException)
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: (retryAttempt, context) =>
                {
                    context.TryGetValue("lastException", out var exceptionObj);
                    var exception = exceptionObj as Exception;
                    return GetDelay(exception, retryAttempt);
                },
                onRetry: (exception, timeSpan, retryCount, _) =>
                {
                    var ctx = new RetryContext(exception, retryCount, timeSpan);
                    OnRetry?.Invoke(ctx);
                });
    }

    public bool ShouldRetry(Exception? exception, int currentRetryCount)
    {
        return exception is TransientSagaException or TimeoutException && currentRetryCount < 3;
    }

    public TimeSpan GetDelay(Exception? exception, int retryCount)
    {
        return exception switch
        {
            TimeoutException => TimeSpan.FromSeconds(1),
            TransientSagaException => TimeSpan.FromSeconds(3),
            _ => TimeSpan.FromSeconds(Math.Pow(2, retryCount))
        };
    }

    public Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        var context = new Context();
        return _policy.ExecuteAsync(async (ctx, ct) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                ctx["lastException"] = ex;
                throw;
            }
        }, context, cancellationToken);
    }
}