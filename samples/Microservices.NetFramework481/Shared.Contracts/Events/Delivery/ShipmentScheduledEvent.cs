using System;
using Lycia.Saga.Messaging;

namespace Shared.Contracts.Events.Delivery;

public sealed class ShipmentScheduledEvent : EventBase
{
    public ShipmentScheduledEvent()
    {
        
    }
    public Guid OrderId { get; set; }
    public Guid ShipmentId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public DateTime ScheduledDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
}
