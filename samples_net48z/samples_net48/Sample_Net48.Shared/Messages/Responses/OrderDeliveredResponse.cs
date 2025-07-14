using Lycia.Messaging;
using Sample_Net48.Shared.Messages.Commands;

using System;
namespace Sample_Net48.Shared.Messages.Responses
{
    public class OrderDeliveredResponse : ResponseBase<ShipOrderCommand>
    {
        public Guid OrderId { get; set; }
    }
}