using Lycia.Messaging;

namespace Sample.Shared.Messages.Events;

public class OrderShippingFailedEvent(string reason, Guid orderId) : FailedEventBase(reason)
{
    public Guid OrderId { get; set; } = orderId;

    public OrderShippingFailedEvent() : this(string.Empty, Guid.Empty) { }
}