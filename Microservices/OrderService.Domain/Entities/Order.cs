namespace OrderService.Domain.Entities;

public sealed record Order
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public DateTime OrderDate { get; init; }
    public IEnumerable<OrderItem> OrderItems { get; init; }
    public decimal TotalAmount { get; init; }

    public static Order Create(Guid orderId, Guid customerId, DateTime orderDate, IEnumerable<OrderItem> orderItems) 
        => new Order
        {
            OrderId = orderId,
            CustomerId = customerId,
            OrderDate = orderDate,
            OrderItems = orderItems,
            TotalAmount = orderItems.Sum(item => item.Price * item.Quantity)
        };
}