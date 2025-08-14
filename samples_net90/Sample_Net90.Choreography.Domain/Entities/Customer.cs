namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Customer : BaseEntity
{
    public Guid CustomerId { get; init; }
    public string FirstName { get; init; }
    public string LastName { get; init; }
    public string Email { get; init; }
    public string PhoneNumber { get; init; }
    public DateTime DateOfBirth { get; init; }
    public IEnumerable<Address> Addresses { get; init; } = new List<Address>();
    public IEnumerable<Card> Cards { get; init; } = new List<Card>();
    public IEnumerable<Order> Orders { get; init; } = new List<Order>();
}
