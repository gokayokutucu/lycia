namespace Lycia.Messaging;

/// <summary>
/// Represents metadata about a failed saga response, used to carry error context.
/// </summary>
public class FailResponse
{
    /// <summary>
    /// Reason or message describing why the operation failed.
    /// </summary>
    public string Reason { get; set; } = null!;

    /// <summary>
    /// Optional name of the exception type that caused the failure.
    /// </summary>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// Timestamp when the failure occurred.
    /// </summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}