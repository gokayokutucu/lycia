namespace Sample.InventoryService.API.Dtos
{
    /// <summary>
    /// Represents an item that has been released from reservation.
    /// </summary>
    public class ReleasedItemDto
    {
        public string ProductId { get; set; }
        public int QuantityReleased { get; set; }

        public ReleasedItemDto(string productId, int quantityReleased)
        {
            ProductId = productId;
            QuantityReleased = quantityReleased;
        }
    }
}
