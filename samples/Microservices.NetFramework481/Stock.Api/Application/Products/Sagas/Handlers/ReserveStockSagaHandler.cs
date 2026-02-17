using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.Logging;
using Sample.Product.NetFramework481.Application.Interfaces;
using Shared.Contracts.Events.Orders;
using Shared.Contracts.Events.Payment;
using Shared.Contracts.Events.Stock;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Product.NetFramework481.Application.Products.Sagas.Handlers;

public sealed class ReserveStockSagaHandler(
    IProductRepository productRepository,
    ILogger<ReserveStockSagaHandler> logger) 
: ReactiveSagaHandler<OrderCreatedEvent>
, ISagaCompensationHandler<PaymentProcessedFailedEvent>
{
    public override async Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Handling OrderCreatedEvent for OrderId: {OrderId}", message.OrderId);
            await productRepository.ReserveStockAsync(message.OrderId, message.Items, cancellationToken);
            logger.LogInformation("Stock reserved successfully for OrderId: {OrderId}", message.OrderId);

            var stockReservedEvent = new StockReservedEvent
            {
                OrderId = message.OrderId,
                CustomerId = message.CustomerId,
                ShippingAddressId = message.ShippingAddressId,
                SavedCardId = message.SavedCardId,
                TotalAmount = message.Items.Sum(item => item.UnitPrice * item.Quantity),
                CustomerName = message.CustomerName,
                CustomerEmail = message.CustomerEmail,
                CustomerPhone = message.CustomerPhone,
                ShippingStreet = message.ShippingStreet,
                ShippingCity = message.ShippingCity,
                ShippingState = message.ShippingState,
                ShippingZipCode = message.ShippingZipCode,
                ShippingCountry = message.ShippingCountry,
                CardHolderName = message.CardHolderName,
                CardLast4Digits = message.CardLast4Digits,
                CardExpiryMonth = message.CardExpiryMonth,
                CardExpiryYear = message.CardExpiryYear
            };
            await Context.Publish(stockReservedEvent, cancellationToken);

            await Context.MarkAsComplete<OrderCreatedEvent>();

            logger.LogInformation("Published StockReservedEvent for OrderId: {OrderId}", message.OrderId);
            logger.LogInformation("Saga completed for OrderId: {OrderId}", message.OrderId);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogError(ex, "💥 [ReserveStockSaga] Saga was canceled");
            await Context.Publish(new StockReservedFailedEvent(ex.Message){ OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsCancelled<OrderCreatedEvent>(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 [ReserveStockSaga] Saga failed with exception");
            await Context.Publish(new StockReservedFailedEvent(ex.Message) { OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsFailed<OrderCreatedEvent>(ex, cancellationToken);
        }
    }

    public async Task CompensateAsync(PaymentProcessedFailedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            await productRepository.ReleaseStockAsync(message.OrderId, message.Items, cancellationToken);
            await Context.Publish(new StockReservedFailedEvent(message.Reason) { OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsCompensated<PaymentProcessedFailedEvent>();
        }
        catch (Exception ex )
        {
            await Context.MarkAsCompensationFailed<PaymentProcessedFailedEvent>(ex);
        }
    }
}
