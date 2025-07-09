using Lycia.Messaging;

namespace Sample.Shared.Messages.Events;

public class OrderCreatedEvent : EventBase
{
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public decimal TotalPrice { get; set; }
}