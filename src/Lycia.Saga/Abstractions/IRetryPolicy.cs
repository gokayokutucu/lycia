namespace Lycia.Saga.Abstractions;

public interface IRetryPolicy
{
    bool ShouldRetry(Exception? exception, int currentRetryCount);
    TimeSpan GetDelay(Exception? exception, int currentRetryCount);
}