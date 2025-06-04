namespace OrderService.Api.Dtos
{
    public class CreateOrderSagaRequestDto
    {
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal TotalPrice { get; set; }
        // public Guid UserId { get; set; } // Optional: if client can provide it, else generate/mock
    }
}
