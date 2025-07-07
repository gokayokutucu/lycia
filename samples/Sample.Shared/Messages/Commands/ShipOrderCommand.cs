using Lycia.Messaging;
using Lycia.Messaging.Attributes;

namespace Sample.Shared.Messages.Commands;

[ApplicationId("ChoreographySampleApp")]
public class ShipOrderCommand : CommandBase
{
    public Guid OrderId { get; set; }
}