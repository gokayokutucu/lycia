using Mapster;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Commands;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class CreateOrderCommandToCreateOrderSagaCommandMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<CreateOrderCommand, CreateOrderSagaCommand>()
            .Map(to => to.CustomerId, from => from.CustomerId)
            .Map(to => to.Products, from => from.Cart)
            ;
    }
}
