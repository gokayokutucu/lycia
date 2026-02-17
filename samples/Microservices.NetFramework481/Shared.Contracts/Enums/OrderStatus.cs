namespace Shared.Contracts.Enums;

/// <summary>
/// Represents the current status of an order in its lifecycle.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Order has been created but not yet processed.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Order is being processed (saga in progress).
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Order has been completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Order has been cancelled by user or system.
    /// </summary>
    Cancelled = 3,

    /// <summary>
    /// Order processing failed (saga compensation triggered).
    /// </summary>
    Failed = 4
}
