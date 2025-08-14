using Lycia.Messaging;

namespace Sample.Shared.Messages.Events;

public sealed class PaymentSucceededEvent : EventBase
{
    public Guid OrderId { get; set; }
}