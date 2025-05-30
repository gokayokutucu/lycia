using System;
using System.Collections.Generic;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Events
{
    public class InventoryUpdateFailedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public List<Guid> FailedProductIds { get; set; }
        public string Reason { get; set; }
    }
}
