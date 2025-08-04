using Lycia.Saga.Handlers;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Events;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Refund.Handlers;

public sealed class RefundPaymentSagaHandler : ReactiveSagaHandler<PaymentProcessFailedSagaEvent>
{
    public override async Task HandleAsync(PaymentProcessFailedSagaEvent message)
    {
        throw new NotImplementedException();
    }
    public override async Task CompensateAsync(PaymentProcessFailedSagaEvent message)
    {
        throw new NotImplementedException();
    }
}
