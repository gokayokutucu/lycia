using Mapster;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Commands;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Process;

public sealed class ProcessPaymentCommandToProcessPaymentSagaCommandMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ProcessPaymentCommand, ProcessPaymentSagaCommand>()
            ;
    }
}
