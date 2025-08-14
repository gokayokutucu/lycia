using Mapster;
using Sample_Net90.Choreography.Domain.Entities;
using Sample_Net90.Choreography.Domain.Enums;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Commands;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Process;

public sealed class ProcessPaymentSagaCommandToPaymentMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Tuple<ProcessPaymentSagaCommand, Guid, Dictionary<CartItem, decimal>>, Domain.Entities.Payment>()
            .Map(to => to.OrderId, from => Guid.CreateVersion7())
            .Map(to => to.Amount, from => from.Item3.Sum(x => x.Key.Quantity * x.Value))
            .Map(to => to.Currency, from => Currency.EUR)
            .Map(to => to.CardId, from => from.Item1.CardId)
            .Map(to => to.Status, from => TransactionStatus.Pending)
            ;
    }
}
