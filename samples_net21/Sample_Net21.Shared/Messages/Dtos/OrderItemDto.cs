using System;

namespace Sample_Net21.Shared.Messages.Dtos
{
    public sealed class OrderItemDto
    {
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }
}
