using Lycia.Saga.Handlers;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Commands;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Events;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class CreatedOrderSagaCommandHandler(ILogger<CreatedOrderSagaCommandHandler> logger, IMapper mapper, IOrderRepository orderRepository)
    : StartReactiveSagaHandler<CreateOrderSagaCommand>
{
    public override async Task HandleStartAsync(CreateOrderSagaCommand message)
    {
        try
        {
            logger.LogInformation("CreatedOrderSagaCommandHandler => HandleStartAsync => Start processing CreateOrderSagaCommand.");

            var order = mapper.Map<Domain.Entities.Order>(message);
            await orderRepository.CreateAsync(order);
            logger.LogInformation("CreatedOrderSagaCommandHandler => HandleStartAsync => Order created with ID: {OrderId}", order.Id);

            var orderCreatedEvent = mapper.Map<OrderCreatedSagaEvent>(order);
            await Context.PublishWithTracking(orderCreatedEvent).ThenMarkAsComplete();

            logger.LogInformation("CreatedOrderSagaCommandHandler => HandleStartAsync => OrderCreatedSagaEvent published successfully and CreateOrderSagaCommand marked as complete for OrderId: {OrderId}", order.Id);
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<CreateOrderSagaCommand>();
            logger.LogError(ex, "CreatedOrderSagaCommandHandler => HandleStartAsync => Error processing CreateOrderSagaCommand.");

            throw new Exception($"CreatedOrderSagaCommandHandler => HandleStartAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
        finally
        {
            logger.LogInformation("CreatedOrderSagaCommandHandler => HandleStartAsync => End processing CreateOrderSagaCommand.");
        }
    }

    public override async Task CompensateStartAsync(CreateOrderSagaCommand message)
    {
        try
        {
            logger.LogInformation("CreatedOrderSagaCommandHandler => CompensateStartAsync => Start compensating CreateOrderSagaCommand.");

            var order = mapper.Map<Domain.Entities.Order>(message);
            await orderRepository.DeleteAsync(order);
            logger.LogInformation("CreatedOrderSagaCommandHandler => CompensateStartAsync => Order with ID: {OrderId} deleted successfully.", order.Id);

            await Context.MarkAsCompensated<CreateOrderSagaCommand>();
            logger.LogInformation("CreatedOrderSagaCommandHandler => CompensateStartAsync => Compensation completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreatedOrderSagaCommandHandler => CompensateStartAsync => Error during compensation of CreateOrderSagaCommand.");

            await Context.MarkAsCompensationFailed<CreateOrderSagaCommand>();
            logger.LogError("CreatedOrderSagaCommandHandler => CompensateStartAsync => Error processing CreateOrderSagaCommand compensation.");

            throw new Exception($"CreatedOrderSagaCommandHandler => CompensateStartAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
}