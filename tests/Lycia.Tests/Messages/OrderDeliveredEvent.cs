using Lycia.Messaging;

namespace Lycia.Tests.Messages;

public class OrderDeliveredEvent : EventBase
{
    public Guid OrderId { get; set; }
    public DateTime DeliveredAt { get; set; } = DateTime.UtcNow;
}