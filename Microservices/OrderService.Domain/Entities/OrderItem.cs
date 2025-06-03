namespace OrderService.Domain.Entities;

public sealed record OrderItem
{
    public Guid ProductId { get; init; }
    public int Price { get; init; }
    public int Quantity { get; init; }

}
