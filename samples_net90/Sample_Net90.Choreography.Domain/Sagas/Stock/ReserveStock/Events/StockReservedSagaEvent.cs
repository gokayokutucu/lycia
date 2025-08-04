using Lycia.Messaging;
using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Sagas.Stock.ReserveStock.Events;

public sealed class StockReservedSagaEvent : EventBase
{
    public Guid ReservationId { get; init; }
    public decimal Total { get; init; }
    public Currency Currency { get; init; }
}
