using Mapster;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Commands;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class CreateOrderSagaCommandToCreateOrderCommandMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<CreateOrderSagaCommand, CreateOrderCommand>()
            .Map(to => to.CustomerId, from => from.CustomerId)
            .Map(to => to.Cart, from => from.Products)
            ;
    }
}
