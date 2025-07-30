using Lycia.Messaging;

namespace Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Events;

public sealed class OrderCreatedSagaEvent : EventBase
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; init; }
    public int Quantity { get; set; }
}
