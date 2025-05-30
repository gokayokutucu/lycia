using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Events
{
    public class LyciaSagaStartedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public decimal TotalPrice { get; set; }
    }
}
