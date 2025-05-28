namespace Sample.PaymentService.API.Dtos.IncomingStock
{
    /// <summary>
    /// Represents a reserved item as part of the StockReservedEvent.
    /// Structure should match the ReservedItemDto published by InventoryService.
    /// </summary>
    public class ReservedItemDto
    {
        public string ProductId { get; set; }
        public int QuantityReserved { get; set; }
    }
}
