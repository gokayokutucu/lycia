using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Address : BaseEntity
{
    public Guid CustomerId { get; set; }

    public string Country { get; init; }
    public string State { get; init; }
    public string City { get; init; }
    public string Street { get; init; }
    public string Building { get; init; }
    public int Floor { get; init; }
    public string Apartment { get; init; }
    public string PostalCode { get; init; }
}
