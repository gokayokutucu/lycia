using Lycia.Messaging;

namespace Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Commands;

public sealed class ProcessPaymentSagaCommand : CommandBase
{
    public Guid OrderId { get; init; }
    public Guid CardId { get; set; }
}
