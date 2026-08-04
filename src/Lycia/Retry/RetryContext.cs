namespace Lycia.Retry;

/// <summary>Describes an exception, attempt number, and delay observed before a retry.</summary>
public sealed record RetryContext(Exception Exception, int Attempt, TimeSpan Delay)
{
    /// <summary>Gets the exception that caused the retry.</summary>
    public Exception Exception { get; } = Exception;
    /// <summary>Gets the zero- or one-based attempt value supplied by the active retry provider.</summary>
    public int Attempt { get; } = Attempt;
    /// <summary>Gets the delay before the next attempt.</summary>
    public TimeSpan Delay { get; } = Delay;
}
