using Lycia.Saga.Handlers;
using MapsterMapper;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Commands;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Events;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class CreateOrderSagaHandler (IMapper mapper, IOrderRepository orderRepository)
    : StartReactiveSagaHandler<CreateOrderSagaCommand>
{
    public override async Task HandleStartAsync(CreateOrderSagaCommand message)
    {
        try
        {
            var order = mapper.Map<Domain.Entities.Order>(message);
            var id = await orderRepository.CreateAsync(order);
            order.Id = id;
            var orderCreatedEvent = mapper.Map<OrderCreatedSagaEvent>(order);

            await Context.PublishWithTracking(orderCreatedEvent).ThenMarkAsComplete();
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<CreateOrderSagaCommand>();
            throw new Exception($"CreateOrderSagaHandler => HandleStartAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    public override async Task CompensateStartAsync(CreateOrderSagaCommand message)
    {
        try
        {
            var order = mapper.Map<Domain.Entities.Order>(message);
            await orderRepository.DeleteAsync(order);

            await Context.MarkAsCompensated<CreateOrderSagaCommand>();
        }
        catch (Exception ex)
        {
            await Context.MarkAsCompensationFailed<CreateOrderSagaCommand>();
            throw new Exception($"CreateOrderSagaHandler => CompensateStartAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
}