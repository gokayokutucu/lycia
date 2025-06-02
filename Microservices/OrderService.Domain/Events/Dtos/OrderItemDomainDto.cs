using System;

namespace OrderService.Domain.Events.Dtos
{
    public class OrderItemDomainDto
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } // Optional
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
    }
}
