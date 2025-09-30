namespace Lycia.Retry;

public interface IRetryPolicy
{
    bool ShouldRetry(Exception? exception, int currentRetryCount);
    TimeSpan GetDelay(Exception? exception, int currentRetryCount);
    ValueTask ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default);
    event Action<RetryContext> OnRetry;
}