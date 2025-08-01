using Lycia.Messaging;

namespace Sample.Shared.Messages.Commands;

public class ProcessPaymentCommand : CommandBase
{
    public Guid OrderId { get; set; }
}