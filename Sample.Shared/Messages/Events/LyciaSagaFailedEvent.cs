using Lycia.Messaging;
using System;

namespace Sample.Shared.Messages.Events
{
    public class LyciaSagaFailedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public string FailureReason { get; set; }
        public string FailedStep { get; set; } // e.g., "Payment", "InventoryCompensation"
    }
}
