using Lycia.Messaging;

namespace Sample.Shared.Messages.Events;

public sealed class InventoryReservedEvent : EventBase
{
    public Guid OrderId { get; set; }
}