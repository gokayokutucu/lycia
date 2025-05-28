namespace Sample.InventoryService.API.Dtos.IncomingOrder
{
    // Represents an item within an order, as expected from OrderCreatedEvent
    public class OrderItemDto
    {
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        // Assuming UnitPrice is not strictly needed for stock reservation,
        // but including it if it was part of the OrderService's OrderCreatedEvent.OrderDetails.Items
        // For now, keeping it minimal to what StockReservationService needs.
        // public decimal UnitPrice { get; set; } 
    }
}
