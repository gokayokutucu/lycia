using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Payment : BaseEntity
{
    public Guid PaymentId { get; init; }
    public Order Order { get; init; } = null!;
    public Guid OrderId { get; init; }
    public Card Card { get; init; } = null!;
    public Guid CardId { get; init; }
    public decimal Amount { get; init; }
    public Currency Currency { get; init; }
    public TransactionStatus Status { get; init; }

}

