using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Events
{
    public class NotificationFailedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public string Reason { get; set; }
    }
}
