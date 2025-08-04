using Lycia.Messaging;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Commands;

public sealed class CreateOrderSagaCommand : CommandBase
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; init; }
    public IEnumerable<CartItem> Cart { get; init; }
    public Guid DeliveryAddress { get; init; }
    public Guid BillingAddress { get; init; }
}
