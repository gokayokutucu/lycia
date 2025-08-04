using Lycia.Saga.Handlers;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Commands;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Events;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class CreateOrderSagaHandler(ILogger<CreateOrderSagaHandler> logger, IMapper mapper, IOrderRepository orderRepository)
    : StartReactiveSagaHandler<CreateOrderSagaCommand>
{
    public override async Task HandleStartAsync(CreateOrderSagaCommand message)
    {
        try
        {
            logger.LogInformation("CreateOrderSagaHandler => HandleStartAsync => Start processing CreateOrderSagaCommand.");

            var order = mapper.Map<Domain.Entities.Order>(message);
            var id = await orderRepository.CreateAsync(order);
            logger.LogInformation("CreateOrderSagaHandler => HandleStartAsync => Order created with ID: {OrderId}", id);

            order = order with { Id = id };
            var orderCreatedEvent = mapper.Map<OrderCreatedSagaEvent>(order);
            await Context.PublishWithTracking(orderCreatedEvent).ThenMarkAsComplete();
            logger.LogInformation("CreateOrderSagaHandler => HandleStartAsync => OrderCreatedSagaEvent published successfully and CreateOrderSagaCommand marked as complete for OrderId: {OrderId}", id);
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<CreateOrderSagaCommand>();
            logger.LogError(ex, "CreateOrderSagaHandler => HandleStartAsync => Error processing CreateOrderSagaCommand.");

            throw new Exception($"CreateOrderSagaHandler => HandleStartAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    public override async Task CompensateStartAsync(CreateOrderSagaCommand message)
    {
        try
        {
            logger.LogInformation("CreateOrderSagaHandler => CompensateStartAsync => Start compensating CreateOrderSagaCommand.");

            var order = mapper.Map<Domain.Entities.Order>(message);
            await orderRepository.DeleteAsync(order);
            logger.LogInformation("CreateOrderSagaHandler => CompensateStartAsync => Order with ID: {OrderId} deleted successfully.", order.Id);

            await Context.MarkAsCompensated<CreateOrderSagaCommand>();
            logger.LogInformation("CreateOrderSagaHandler => CompensateStartAsync => Compensation completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreateOrderSagaHandler => CompensateStartAsync => Error during compensation of CreateOrderSagaCommand.");

            await Context.MarkAsCompensationFailed<CreateOrderSagaCommand>();
            logger.LogError("CreateOrderSagaHandler => CompensateStartAsync => Error processing CreateOrderSagaCommand compensation.");

            throw new Exception($"CreateOrderSagaHandler => CompensateStartAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
}