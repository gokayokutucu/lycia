using Mapster;
using Sample_Net90.Choreography.Domain.Sagas.Stock.ReserveStock.Events;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Process.Mapping;

public sealed class PaymentStockReservedSagaEventMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Domain.Entities.Payment, StockReservedSagaEvent>()
            //.Map(to => to.OrderId, from => from.OrderId)
            //.Map(to => to.ProductId, from => from.ProductId)
            //.Map(to => to.Quantity, from => from.Quantity)
            ;
    }
}
