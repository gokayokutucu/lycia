using Lycia.Messaging;

namespace Sample.Shared.Messages.Commands;

public class ReserveInventoryCommand : CommandBase
{
    public Guid OrderId { get; set; }
}