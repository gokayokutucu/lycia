using Lycia.Messaging;
using Lycia.Messaging.Attributes;

namespace Sample.Shared.Messages.Events;

[ApplicationId("ChoreographySampleApp")]
public class OrderShippedEvent : EventBase
{
    public Guid OrderId { get; set; }
    public DateTime ShippedAt { get; set; } = DateTime.UtcNow;
    public Guid ShipmentTrackId { get; set; }
}