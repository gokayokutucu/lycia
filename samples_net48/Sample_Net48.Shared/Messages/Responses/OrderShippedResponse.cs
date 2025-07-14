using Lycia.Messaging;
using Sample_Net48.Shared.Messages.Events;

using System;
namespace Sample_Net48.Shared.Messages.Responses
{
    public class OrderShippedResponse : ResponseBase<OrderCreatedEvent>
    {
        public Guid OrderId { get; set; }
    }
}