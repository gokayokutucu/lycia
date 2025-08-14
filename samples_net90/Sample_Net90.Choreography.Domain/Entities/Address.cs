namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Address : BaseEntity
{
    public  Guid AddressId { get; init; }
    public Customer Customer { get; init; } = null!;
    public Guid CustomerId { get; init; }
    public string Country { get; init; }
    public string State { get; init; }
    public string City { get; init; }
    public string Street { get; init; }
    public string Building { get; init; }
    public int Floor { get; init; }
    public string Apartment { get; init; }
    public string PostalCode { get; init; }
}
