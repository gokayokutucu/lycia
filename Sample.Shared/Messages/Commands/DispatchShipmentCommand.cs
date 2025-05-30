using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Commands
{
    public class DispatchShipmentCommand : CommandBase
    {
        public Guid OrderId { get; set; }
        public string ShippingAddress { get; set; }
    }
}
