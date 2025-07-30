using Lycia.Saga.Handlers;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Events;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class ReserveStockSagaHandler(ILogger<ReserveStockSagaHandler> logger, IMapper mapper, IStockRepository stockRepository)
    : ReactiveSagaHandler<OrderCreatedSagaEvent>
{
    public override async Task HandleAsync(OrderCreatedSagaEvent orderCreatedEvent)
    {
        try
        {
            logger.LogInformation("ReserveStockSagaHandler => HandleAsync => Start processing OrderCreatedSagaEvent for OrderId: {OrderId}"
                , orderCreatedEvent.OrderId);

            var isAvailable = await stockRepository.IsStockAvailableAsync(orderCreatedEvent.ProductId, orderCreatedEvent.Quantity);
            if (!isAvailable)
            {
                logger.LogWarning("ReserveStockSagaHandler => HandleAsync => Insufficient stock for ProductId: {ProductId}, Quantity: {Quantity}"
                    , orderCreatedEvent.ProductId
                    , orderCreatedEvent.Quantity);
                throw new InvalidOperationException($"Insufficient stock for ProductId: {orderCreatedEvent.ProductId}, Quantity: {orderCreatedEvent.Quantity}");
            }

            var stock = mapper.Map<Domain.Entities.Stock>(orderCreatedEvent);
            await stockRepository.ReserveStockAsync(orderCreatedEvent.OrderId, orderCreatedEvent.ProductId, orderCreatedEvent.Quantity);
            logger.LogInformation("ReserveStockSagaHandler => HandleAsync => Stock reserved successfully for OrderId: {OrderId}, ProductId: {ProductId}, Quantity: {Quantity}"
                , orderCreatedEvent.OrderId
                , orderCreatedEvent.ProductId
                , orderCreatedEvent.Quantity);

            await Context.MarkAsComplete<OrderCreatedSagaEvent>();
            logger.LogInformation("ReserveStockSagaHandler => HandleAsync => OrderCreatedSagaEvent marked as complete for OrderId: {OrderId}"
                , orderCreatedEvent.OrderId);

            //the End of the saga step, marking it as complete
            //var stockReservedSagaEvent = mapper.Map<StockReservedSagaEvent>(orderCreatedEvent);
            //await Context.PublishWithTracking(stockReservedSagaEvent).ThenMarkAsComplete();
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<OrderCreatedSagaEvent>();
            logger.LogError(ex, "ReserveStockSagaHandler => HandleAsync => Error processing OrderCreatedSagaEvent.");

            throw new Exception($"ReserveStockSagaHandler => HandleAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    public override async Task CompensateAsync(OrderCreatedSagaEvent message)
    {
        try
        {
            logger.LogInformation("ReserveStockSagaHandler => CompensateAsync => Start compensating for OrderCreatedSagaEvent with OrderId: {OrderId}"
                , message.OrderId);

            var stock = mapper.Map<Domain.Entities.Stock>(message);
            await stockRepository.ReleaseStockAsync(message.OrderId, message.ProductId, message.Quantity);
            logger.LogInformation("ReserveStockSagaHandler => CompensateAsync => Stock released successfully for OrderId: {OrderId}, ProductId: {ProductId}, Quantity: {Quantity}"
                , message.OrderId
                , message.ProductId
                , message.Quantity);

            await Context.MarkAsCompensated<OrderCreatedSagaEvent>();
            logger.LogInformation("ReserveStockSagaHandler => CompensateAsync => Compensation completed successfully for OrderId: {OrderId}"
                , message.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ReserveStockSagaHandler => CompensateAsync => Error during compensation of OrderCreatedSagaEvent with OrderId: {OrderId}"
                , message.OrderId);

            await Context.MarkAsCompensationFailed<OrderCreatedSagaEvent>();
            logger.LogError("ReserveStockSagaHandler => CompensateAsync => Error processing OrderCreatedSagaEvent compensation for OrderId: {OrderId}"
                , message.OrderId);

            throw new Exception($"ReserveStockSagaHandler => CompensateAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
}