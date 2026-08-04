namespace Lycia.Retry;

/// <summary>Defines retry classification, delay calculation, execution, and observation for saga operations.</summary>
public interface IRetryPolicy
{
    /// <summary>Determines whether an exception should be retried at the current attempt.</summary>
    bool ShouldRetry(Exception? exception, int currentRetryCount);
    /// <summary>Calculates the delay before the next attempt.</summary>
    TimeSpan GetDelay(Exception? exception, int currentRetryCount);
    /// <summary>Executes an asynchronous operation according to this policy.</summary>
    ValueTask ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default);
    /// <summary>Occurs immediately before a retry attempt is made.</summary>
    event Action<RetryContext> OnRetry;
}
