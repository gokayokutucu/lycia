namespace Sample.OrderService.API.Dtos.IncomingInventory
{
    /// <summary>
    /// Represents details of a released item, part of StockReleasedEventDto.
    /// </summary>
    public class ReleasedItemDetailDto
    {
        public string ProductId { get; set; }
        public int QuantityReleased { get; set; }
    }
}
