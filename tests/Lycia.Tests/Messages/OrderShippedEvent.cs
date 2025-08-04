using Lycia.Messaging;

namespace Lycia.Tests.Messages;

public class OrderShippedEvent : EventBase
{
    public Guid OrderId { get; set; }
    public DateTime ShippedAt { get; set; } = DateTime.UtcNow;
    public Guid ShipmentTrackId { get; set; }
}