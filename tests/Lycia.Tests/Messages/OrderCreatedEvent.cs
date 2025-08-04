using Lycia.Messaging;

namespace Lycia.Tests.Messages;

public class OrderCreatedEvent: EventBase
{
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public decimal TotalPrice { get; set; }
}