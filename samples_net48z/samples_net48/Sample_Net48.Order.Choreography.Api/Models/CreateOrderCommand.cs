using Lycia.Messaging;
using System;

namespace Sample_Net48.Order.Choreography.Api.Models
{
    public class CreateOrderCommand : CommandBase
    {
        public Guid OrderId { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public decimal TotalPrice { get; set; }
    }
}