using Lycia.Messaging;

namespace Sample.Shared.Messages.Commands;

public class ShipOrderCommand : CommandBase
{
    public Guid OrderId { get; set; }
}