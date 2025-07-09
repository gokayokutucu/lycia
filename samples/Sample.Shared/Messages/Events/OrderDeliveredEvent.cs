using Lycia.Messaging;

namespace Sample.Shared.Messages.Events;

public class OrderDeliveredEvent : EventBase
{
    public Guid OrderId { get; set; }
    public DateTime DeliveredAt { get; set; } = DateTime.UtcNow;
}