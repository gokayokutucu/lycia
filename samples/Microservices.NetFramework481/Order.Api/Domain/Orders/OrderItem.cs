using Sample.Order.NetFramework481.Domain.Common;
using System;

namespace Sample.Order.NetFramework481.Domain.Orders;

public sealed class OrderItem : Entity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
