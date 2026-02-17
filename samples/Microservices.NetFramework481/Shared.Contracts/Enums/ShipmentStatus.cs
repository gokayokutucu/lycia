namespace Shared.Contracts.Enums;

/// <summary>
/// Represents the current status of a shipment in its lifecycle.
/// </summary>
public enum ShipmentStatus
{
    /// <summary>
    /// Shipment is pending scheduling.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Shipment has been scheduled for delivery.
    /// </summary>
    Scheduled = 1,

    /// <summary>
    /// Shipment is in transit to customer.
    /// </summary>
    InTransit = 2,

    /// <summary>
    /// Shipment has been delivered to customer.
    /// </summary>
    Delivered = 3,

    /// <summary>
    /// Shipment has been cancelled.
    /// </summary>
    Cancelled = 4
}
