namespace Shared.Contracts.Enums;

/// <summary>
/// Represents the current status of a payment transaction.
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// Payment is pending processing.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Payment has been authorized but not yet captured.
    /// </summary>
    Authorized = 1,

    /// <summary>
    /// Payment has been successfully captured.
    /// </summary>
    Captured = 2,

    /// <summary>
    /// Payment processing failed.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Payment has been refunded to customer.
    /// </summary>
    Refunded = 4
}
