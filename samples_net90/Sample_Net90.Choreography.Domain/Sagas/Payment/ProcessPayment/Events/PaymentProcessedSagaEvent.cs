using Lycia.Messaging;
using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Events;

public sealed class PaymentProcessedSagaEvent : EventBase
{
    public Guid PaymentId { get; init; }
    public Guid OrderId { get; init; }
}
