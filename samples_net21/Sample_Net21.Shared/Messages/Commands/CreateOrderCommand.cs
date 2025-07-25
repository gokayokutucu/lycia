using Lycia.Messaging;
using Sample_Net21.Shared.Messages.Dtos;
using System;
using System.Collections.Generic;

namespace Sample_Net21.Shared.Messages.Commands
{
    public class CreateOrderCommand : CommandBase
    {
        public Guid OrderId { get; set; }
        public Guid CustomerId { get; set; }
        public string ShippingAddress { get; set; } = string.Empty;
        public decimal OrderTotal { get; set; }
        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
    }
}