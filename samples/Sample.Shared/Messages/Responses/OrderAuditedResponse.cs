using Lycia.Messaging;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Responses;

public class OrderAuditedResponse : ResponseBase<OrderCreatedEvent>
{
    public Guid OrderId { get; set; }
}