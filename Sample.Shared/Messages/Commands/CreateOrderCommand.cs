using Lycia.Messaging;
using Lycia.Messaging.Attributes;

namespace Sample.Shared.Messages.Commands;

/// <summary>
/// Command to initiate an order creation saga.
/// </summary>
[ApplicationId("ChoreographySampleApp")]
public class CreateOrderCommand : CommandBase
{
    public Guid OrderId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public decimal TotalPrice { get; set; }
}