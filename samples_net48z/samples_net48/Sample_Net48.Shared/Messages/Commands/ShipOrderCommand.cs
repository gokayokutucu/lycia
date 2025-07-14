using Lycia.Messaging;
using System;

namespace Sample_Net48.Shared.Messages.Commands
{
    public class ShipOrderCommand : CommandBase
    {
        public Guid OrderId { get; set; }
    }
}