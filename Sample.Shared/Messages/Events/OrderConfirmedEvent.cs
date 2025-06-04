using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Events
{
    public class OrderConfirmedEvent : EventBase
    {
        public Guid OrderId { get; set; }
    }
}
