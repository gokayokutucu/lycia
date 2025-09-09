using Lycia.Saga.Exceptions;
using Polly;
using Polly.Retry;
using IRetryPolicy = Lycia.Saga.Abstractions.IRetryPolicy;

namespace Lycia.Extensions.Retry;

public class PollyRetryPolicy : IRetryPolicy
{
    private readonly AsyncRetryPolicy _policy;

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
                    Console.WriteLine($"[Retry] Attempt {retryCount} after {timeSpan.TotalSeconds}s: {exception.Message}");
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