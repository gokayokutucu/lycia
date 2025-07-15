using Lycia.Messaging;
using Sample_Net481.Shared.Messages.Events;

using System;
namespace Sample_Net481.Shared.Messages.Responses
{
    public class OrderShippedResponse : ResponseBase<OrderCreatedEvent>
    {
        public Guid OrderId { get; set; }
    }
}