using Lycia.Messaging;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Events;

public sealed class OrderCreatedSagaEvent : EventBase
{
    public Guid OrderId { get; init; }
    public IEnumerable<CartItem> Cart { get; init; }
    public Guid DeliveryAddress { get; init; }
    public Guid BillingAddress { get; init; }
}
