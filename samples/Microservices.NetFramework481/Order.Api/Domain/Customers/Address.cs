using Sample.Order.NetFramework481.Domain.Common;
using System;

namespace Sample.Order.NetFramework481.Domain.Customers;

public sealed class Address : Entity
{
    public Guid CustomerId { get; set; }
    public string Street { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public string ZipCode { get; set; }
    public string Country { get; set; }
    public bool IsDefault { get; set; }
}
