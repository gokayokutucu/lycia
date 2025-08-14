using Lycia.Messaging;

namespace Sample.Shared.Messages.Events;

public class PaymentProcessedEvent : EventBase
{
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public decimal PaymentId { get; set; }
}