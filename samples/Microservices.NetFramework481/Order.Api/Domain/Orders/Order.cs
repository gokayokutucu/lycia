using Sample.Order.NetFramework481.Domain.Common;
using System;
using System.Collections.Generic;

namespace Sample.Order.NetFramework481.Domain.Orders;

public sealed class Order : Entity
{
    public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    public Guid CustomerId { get; set; }
    public Guid ShippingAddressId { get; set; }
    public Guid SavedCardId { get; set; }   
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
}
