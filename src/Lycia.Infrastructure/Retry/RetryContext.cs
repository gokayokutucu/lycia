namespace Lycia.Infrastructure.Retry;

public sealed class RetryContext
{
    public Exception Exception { get; private set; }
    public int Attempt { get; private set; }
    public TimeSpan Delay { get; private set; }

    public RetryContext(Exception exception, int attempt, TimeSpan delay)
    {
        Exception = exception;
        Attempt = attempt;
        Delay = delay;
    }
}