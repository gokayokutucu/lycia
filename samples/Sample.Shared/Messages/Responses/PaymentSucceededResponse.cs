using Lycia.Messaging;
using Sample.Shared.Messages.Commands;

namespace Sample.Shared.Messages.Responses;

public class PaymentSucceededResponse : ResponseBase<ProcessPaymentCommand>
{
    public Guid OrderId { get; set; }
}