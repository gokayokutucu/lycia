using Lycia.Messaging;

namespace Sample.Shared.Messages.Events;

public class PaymentFailedEvent (string reason, Guid orderId) : FailedEventBase(reason)
{
public Guid OrderId { get; set; } = orderId;

public PaymentFailedEvent() : this(string.Empty, Guid.Empty) { }
}