using Lycia.Messaging;
using Lycia.Messaging.Attributes;

namespace Sample.Shared.Messages.Events;

[ApplicationId("ChoreographySampleApp")]
public class OrderShippingFailedEvent(string reason, Guid orderId) : FailedEventBase(reason)
{
    public Guid OrderId { get; set; } = orderId;

    public OrderShippingFailedEvent() : this(string.Empty, Guid.Empty) { }
}