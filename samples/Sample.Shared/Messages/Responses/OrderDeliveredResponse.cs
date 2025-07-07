using Lycia.Messaging;
using Sample.Shared.Messages.Commands;

namespace Sample.Shared.Messages.Responses;

public class OrderDeliveredResponse : ResponseBase<ShipOrderCommand>
{
    public Guid OrderId { get; set; }
}