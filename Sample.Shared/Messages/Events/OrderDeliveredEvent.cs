using Lycia.Messaging;
using Lycia.Messaging.Attributes;

namespace Sample.Shared.Messages.Events;

[ApplicationId("ChoreographySampleApp")]
public class OrderDeliveredEvent : EventBase
{
    public Guid OrderId { get; set; }
    public DateTime DeliveredAt { get; set; } = DateTime.UtcNow;
}