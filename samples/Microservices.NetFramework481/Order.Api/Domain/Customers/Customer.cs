using Sample.Order.NetFramework481.Domain.Common;
using System.Collections.Generic;

namespace Sample.Order.NetFramework481.Domain.Customers;

public sealed class Customer : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public List<Address> Addresses { get; set; } = [];
    public List<Card> Cards { get; set; } = [];
}
