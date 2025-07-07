using Lycia.Messaging;
using Sample.Shared.Messages.Commands;

namespace Sample.Shared.Messages.Responses;

/// <summary>
/// Response indicating that an order was successfully created.
/// </summary>
public class OrderCreatedResponse : ResponseBase<CreateOrderCommand>
{
    public Guid OrderId { get; set; }
}