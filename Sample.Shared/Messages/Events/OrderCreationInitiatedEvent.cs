using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Events
{
    public class OrderCreationInitiatedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal TotalPrice { get; set; }
    }
}
