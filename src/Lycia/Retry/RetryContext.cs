namespace Lycia.Retry;

public sealed record RetryContext(Exception Exception, int Attempt, TimeSpan Delay)
{
    public Exception Exception { get; } = Exception;
    public int Attempt { get; } = Attempt;
    public TimeSpan Delay { get; } = Delay;
}