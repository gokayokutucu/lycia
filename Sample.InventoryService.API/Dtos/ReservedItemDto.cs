namespace Sample.InventoryService.API.Dtos
{
    public class ReservedItemDto
    {
        public string ProductId { get; set; }
        public int QuantityReserved { get; set; }
        // Could add original quantity requested if it differs from quantity reserved
        // public int QuantityRequested { get; set; } 

        public ReservedItemDto(string productId, int quantityReserved)
        {
            ProductId = productId;
            QuantityReserved = quantityReserved;
        }
    }
}
