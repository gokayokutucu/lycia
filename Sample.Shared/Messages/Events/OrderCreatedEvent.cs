using Lycia.Messaging;
using Lycia.Messaging.Attributes;

namespace Sample.Shared.Messages.Events;

[ApplicationId("ChoreographySampleApp")]
public class OrderCreatedEvent : EventBase
{
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public decimal TotalPrice { get; set; }
}