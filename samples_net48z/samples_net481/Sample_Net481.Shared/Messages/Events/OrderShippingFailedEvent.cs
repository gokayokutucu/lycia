using Lycia.Messaging;
using System;

namespace Sample_Net481.Shared.Messages.Events
{
    public class OrderShippingFailedEvent : FailedEventBase
    {
        public Guid OrderId { get; set; }

        public OrderShippingFailedEvent() : this(string.Empty, Guid.Empty) { }

        public OrderShippingFailedEvent(string reason, Guid orderId)
            : base(reason)
        {
            OrderId = orderId;
        }
    }
}