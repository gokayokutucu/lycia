using System.Collections.Generic;

namespace Sample.InventoryService.API.Dtos.IncomingOrder
{
    // Represents the OrderDetails part of OrderCreatedEvent
    public class OrderDetailsDto
    {
        // CustomerId and TotalAmount might not be strictly needed by InventoryService's
        // stock reservation logic, but they are part of the OrderService's event structure.
        // Including them for complete deserialization.
        public string CustomerId { get; set; } 
        public List<OrderItemDto> Items { get; set; }
        public decimal TotalAmount { get; set; }

        public OrderDetailsDto()
        {
            Items = new List<OrderItemDto>();
        }
    }
}
