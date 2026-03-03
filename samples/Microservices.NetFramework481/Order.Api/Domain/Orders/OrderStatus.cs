namespace Sample.Order.NetFramework481.Domain.Orders;

/// <summary>
/// Order status enumeration.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Order has been created but not yet processed.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Order is being processed.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Order has been completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Order has failed.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Order has been cancelled.
    /// </summary>
    Cancelled = 4
}
