using Lycia.Saga.Handlers;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Commands;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Events;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class CreateOrderSagaCommandHandler(
    ILogger<CreateOrderSagaCommandHandler> logger, 
    IMapper mapper, 
    IOrderRepository orderRepository,
    ICustomerRepository customerRepository)
    : StartReactiveSagaHandler<CreateOrderSagaCommand>
{
    public override async Task HandleStartAsync(CreateOrderSagaCommand message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Start processing CreateOrderSagaCommand.");

            if (!await customerRepository.CustomerExistsAsync(message.CustomerId))
            {
                logger.LogWarning("Customer does not exist for OrderId: {OrderId}", message.OrderId);
                throw new Exception($"Customer with ID {message.CustomerId} does not exist.");
            }
            if (!await customerRepository.AddressExistsAsync(message.DeliveryAddress))
            {
                logger.LogWarning("Delivery address does not exist for OrderId: {OrderId}", message.OrderId);
                throw new Exception($"Delivery address with ID {message.DeliveryAddress} does not exist.");
            }
            if (!await customerRepository.AddressExistsAsync(message.BillingAddress))
            {
                logger.LogWarning("Billing address does not exist for OrderId: {OrderId}", message.OrderId);
                throw new Exception($"Billing address with ID {message.BillingAddress} does not exist.");
            }

            var order = mapper.Map<Domain.Entities.Order>(message);
            await orderRepository.CreateAsync(order);
            logger.LogInformation("Order created with ID: {OrderId}", order.OrderId);

            var orderCreatedEvent = mapper.Map<OrderCreatedSagaEvent>(order);
            await Context.PublishWithTracking(orderCreatedEvent).ThenMarkAsComplete();

            logger.LogInformation("OrderCreatedSagaEvent published successfully and CreateOrderSagaCommand marked as complete for OrderId: {OrderId}", order.OrderId);
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<CreateOrderSagaCommand>();
            logger.LogError(ex, "Error processing CreateOrderSagaCommand.");

            throw new Exception($"Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
        finally
        {
            logger.LogInformation("End processing CreateOrderSagaCommand.");
        }
    }

    public override async Task CompensateStartAsync(CreateOrderSagaCommand message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Start compensating CreateOrderSagaCommand.");

            var order = mapper.Map<Domain.Entities.Order>(message);
            await orderRepository.DeleteAsync(order);
            logger.LogInformation("Order with ID: {OrderId} deleted successfully.", order.OrderId);

            await Context.MarkAsCompensated<CreateOrderSagaCommand>();
            logger.LogInformation("Compensation completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during compensation of CreateOrderSagaCommand.");

            await Context.MarkAsCompensationFailed<CreateOrderSagaCommand>();
            logger.LogError("Error processing CreateOrderSagaCommand compensation.");

            throw new Exception($"Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
}