namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Card : BaseEntity
{
    public Guid CustomerId { get; init; }

    public string CardNumber { get; init; }
    public string CardHolderName { get; init; }
    public DateTime ExpirationDate { get; init; }
    public string CVV { get; init; }
}
