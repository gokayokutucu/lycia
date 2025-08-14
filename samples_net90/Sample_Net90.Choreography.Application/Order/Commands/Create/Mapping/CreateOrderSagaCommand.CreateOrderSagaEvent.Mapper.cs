using Mapster;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Commands;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Events;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public class CreateOrderSagaCommandToOrderCreatedSagaEventMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<CreateOrderSagaCommand, OrderCreatedSagaEvent>()
            ;
    }
}
