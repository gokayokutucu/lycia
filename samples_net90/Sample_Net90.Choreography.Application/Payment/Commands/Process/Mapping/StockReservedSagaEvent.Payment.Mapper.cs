using Mapster;
using Sample_Net90.Choreography.Domain.Sagas.Stock.ReserveStock.Events;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Process;

public sealed class StockReservedSagaEventPaymentMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<StockReservedSagaEvent, Domain.Entities.Payment>()
            //.Map(to => to.OrderId, from => from.OrderId)
            //.Map(to => to.ProductId, from => from.ProductId)
            //.Map(to => to.Quantity, from => from.Quantity)
            ;
    }
}
