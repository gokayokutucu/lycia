using Lycia.Messaging;
using System;


namespace Sample_Net21.Shared.Messages.Events
{
    public sealed class ShipmentScheduleFailedEvent : EventBase
    {
        public Guid OrderId { get; set; }
    }
}