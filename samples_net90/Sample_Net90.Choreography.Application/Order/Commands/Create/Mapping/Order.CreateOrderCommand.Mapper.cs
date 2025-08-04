using Mapster;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public class OrderToCreateOrderCommandMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Domain.Entities.Order, CreateOrderCommand>();
    }
}
