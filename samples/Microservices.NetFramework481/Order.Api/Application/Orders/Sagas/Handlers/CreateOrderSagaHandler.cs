using Lycia.Saga.Messaging.Handlers;
using Sample.Order.NetFramework481.Application.Interfaces;
using Shared.Contracts.Commands;
using Shared.Contracts.Events.Orders;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Lycia.Saga.Abstractions.Handlers;
using Shared.Contracts.Events.Stock;

namespace Sample.Order.NetFramework481.Application.Orders.Sagas.Handlers;

public sealed class CreateOrderSagaHandler(
    IOrderRepository orderRepository,
    ICustomerRepository customerRepository,
    IAddressRepository addressRepository,
    ICardRepository cardRepository,
    ILogger<CreateOrderSagaHandler> logger)
: StartReactiveSagaHandler<CreateOrderSagaCommand>
, ISagaCompensationHandler<StockReservedFailedEvent>
{
    public override async Task HandleStartAsync(CreateOrderSagaCommand message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("📍 [CreateOrderSaga] Fetching customer, address, and card...");
            var customer = await customerRepository.GetByIdAsync(message.CustomerId, cancellationToken);
            var address = await addressRepository.GetByIdAsync(message.ShippingAddressId, cancellationToken);
            var savedCard = await cardRepository.GetByIdAsync(message.SavedCardId, cancellationToken);

            if (customer == null)
                throw new InvalidOperationException($"Customer with Id {message.CustomerId} not found");
            if (address == null)
                throw new InvalidOperationException($"Address with Id {message.ShippingAddressId} not found");
            if (savedCard == null)
                throw new InvalidOperationException($"SavedCard with Id {message.SavedCardId} not found");

            var order = new Domain.Orders.Order
            {
                CustomerId = message.CustomerId,
                ShippingAddressId = message.ShippingAddressId,
                SavedCardId = message.SavedCardId,
                Status = Domain.Orders.OrderStatus.Pending,
                Items = [.. message.Items.Select(dto =>
                new Domain.Orders.OrderItem
                {
                    ProductId = dto.ProductId,
                    ProductName = dto.ProductName,
                    Quantity = dto.Quantity,
                    UnitPrice = dto.UnitPrice
                })],
                TotalAmount = message.Items.Sum(dto => dto.Quantity * dto.UnitPrice)

            };
            await orderRepository.SaveAsync(order, cancellationToken);

            var orderCreatedEvent = new OrderCreatedEvent
            {
                OrderId = order.Id,
                Items = message.Items,
                CustomerId = order.CustomerId,
                CustomerName = customer.Name,
                CustomerEmail = customer.Email,
                CustomerPhone = customer.Phone,
                ShippingAddressId = order.ShippingAddressId,
                ShippingStreet = address.Street,
                ShippingCity = address.City,
                ShippingState = address.State,
                ShippingZipCode = address.ZipCode,
                ShippingCountry = address.Country,
                SavedCardId = order.SavedCardId,
                CardHolderName = savedCard.CardHolderName,
                CardLast4Digits = savedCard.Last4Digits,
                CardExpiryMonth = savedCard.ExpiryMonth,
                CardExpiryYear = savedCard.ExpiryYear
            };
            await Context.Publish(orderCreatedEvent, cancellationToken);

            await Context.MarkAsComplete<CreateOrderSagaCommand>();
        }
        catch (OperationCanceledException ex)
        {
            logger.LogError(ex, "💥 [CreateOrderSaga] Saga was canceled");
            await Context.MarkAsCancelled<CreateOrderSagaCommand>(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 [CreateOrderSaga] Saga failed with exception");
            await Context.MarkAsFailed<CreateOrderSagaCommand>(ex, cancellationToken);
        }
    }

    public override async Task CompensateStartAsync(CreateOrderSagaCommand message, CancellationToken cancellationToken = default)
    {
        try
        {
            await Context.MarkAsCompensated<CreateOrderSagaCommand>();
        }
        catch (Exception ex)
        {
            await Context.MarkAsCompensationFailed<CreateOrderSagaCommand>(ex);
        }
    }

    public async Task CompensateAsync(StockReservedFailedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            var order = await orderRepository.GetByIdAsync(message.OrderId, cancellationToken);
            if (order != null)
            {
                order.Status = Domain.Orders.OrderStatus.Failed;
                await orderRepository.UpdateAsync(order, cancellationToken);
            }

            await Context.MarkAsCompensated<StockReservedFailedEvent>();
        }
        catch (Exception ex)
        {
            await Context.MarkAsCompensationFailed<StockReservedFailedEvent>(ex);
        }
    }
}
