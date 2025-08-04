using Mapster;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public class CreateOrderCommandToOrderMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<CreateOrderCommand, Domain.Entities.Order>();
    }
}
