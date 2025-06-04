using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Events
{
    public class StockCheckRequestedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public string ProductId { get; set; }
        public int Quantity { get; set; }
    }
}
