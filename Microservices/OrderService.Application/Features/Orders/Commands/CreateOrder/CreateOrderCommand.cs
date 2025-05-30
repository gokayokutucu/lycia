using System;
using System.Collections.Generic;
using MediatR;

namespace OrderService.Application.Features.Orders.Commands.CreateOrder
{
    // DTO for items within the command
    public class OrderItemDto
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } // Optional, could be enriched later
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
    }

    public class CreateOrderCommand : IRequest<Guid>
    {
        public Guid UserId { get; set; }
        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
        
        // Potential future properties:
        // public string ShippingAddress { get; set; }
        // public PaymentDetailsDto PaymentInfo { get; set; }
    }
}
