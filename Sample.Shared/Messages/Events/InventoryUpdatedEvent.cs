using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Events
{
    public class InventoryUpdatedEvent : EventBase
    {
        public Guid OrderId { get; set; }
    }
}
