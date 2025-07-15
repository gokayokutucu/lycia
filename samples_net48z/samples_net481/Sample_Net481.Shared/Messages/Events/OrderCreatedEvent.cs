using Lycia.Messaging;
using System;

namespace Sample_Net481.Shared.Messages.Events
{
    public class OrderCreatedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public decimal TotalPrice { get; set; }
    }
}