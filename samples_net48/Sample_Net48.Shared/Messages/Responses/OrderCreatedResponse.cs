using Lycia.Messaging;
using Sample_Net48.Shared.Messages.Commands;
using System;

namespace Sample_Net48.Shared.Messages.Responses
{
    /// <summary>
    /// Response indicating that an order was successfully created.
    /// </summary>
    public class OrderCreatedResponse : ResponseBase<CreateOrderCommand>
    {
        public Guid OrderId { get; set; }
    }
}