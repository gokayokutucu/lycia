using Sample.Order.NetFramework481.Domain.Common;
using System;

namespace Sample.Order.NetFramework481.Domain.Customers;

public sealed class Card : Entity
{
    public Guid CustomerId { get; set; }
    public string Last4Digits { get; set; }
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string CardHolderName { get; set; }
    public bool IsDefault { get; set; }
}
