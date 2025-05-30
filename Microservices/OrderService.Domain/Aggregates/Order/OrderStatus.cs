namespace OrderService.Domain.Aggregates.Order
{
    public enum OrderStatus
    {
        Pending,
        Processing,
        Paid,
        Shipped,
        Completed,
        Cancelled,
        Failed
    }
}
