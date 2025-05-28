using System.Collections.Generic;

namespace Sample.OrderService.API.Events
{
    public class OrderDetailsDto
    {
        public string CustomerId { get; set; }
        public List<OrderItemDto> Items { get; set; }
        public decimal TotalAmount { get; set; }

        public OrderDetailsDto()
        {
            Items = new List<OrderItemDto>();
        }
    }

    public class OrderItemDto
    {
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}
