using Mapster;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Commands;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public class OrderToCreateOrderSagaCommandMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Domain.Entities.Order, CreateOrderSagaCommand>()
            .Map(to => to.CustomerId, from => from.CustomerId)
            .Map(to => to.Products, from => from.Products)
            ;
    }
}
