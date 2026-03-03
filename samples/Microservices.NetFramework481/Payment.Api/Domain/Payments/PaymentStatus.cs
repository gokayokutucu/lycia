namespace Sample.Payment.NetFramework481.Domain.Payments;

/// <summary>
/// Payment status enumeration.
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// Payment is pending.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Payment is processing.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Payment is completed.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Payment failed.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Payment refunded.
    /// </summary>
    Refunded = 4
}
