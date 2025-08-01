using Lycia.Messaging;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Responses;

public class OrderShippedResponse : ResponseBase<ShipOrderCommand>
{
    public Guid OrderId { get; set; }
}

public class ShippedOrderResponse : ResponseBase<OrderCreatedEvent>
{
    public Guid OrderId { get; set; }
}