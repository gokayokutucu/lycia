using Lycia.Messaging;
using System;

namespace Sample_Net48.Shared.Messages.Commands
{
    /// <summary>
    /// Command to initiate an order creation saga.
    /// </summary>
    public class CreateOrderCommand : CommandBase
    {
        public Guid OrderId { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public decimal TotalPrice { get; set; }
    }
}