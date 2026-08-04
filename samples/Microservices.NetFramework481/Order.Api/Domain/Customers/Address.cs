using Sample.Order.NetFramework481.Domain.Common;
using System;

namespace Sample.Order.NetFramework481.Domain.Customers;

public sealed class Address : Entity
{
    public Guid CustomerId { get; set; }
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
