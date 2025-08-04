using Lycia.Messaging;
using Sample.Shared.Messages.Commands;

namespace Sample.Shared.Messages.Responses;

public class InventoryReservedResponse : ResponseBase<ReserveInventoryCommand>
{
    public Guid OrderId { get; set; }
}