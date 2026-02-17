using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.Logging;
using Sample.Payment.NetFramework481.Application.Interfaces;
using Sample.Payment.NetFramework481.Domain.Payments;
using Shared.Contracts.Events.Delivery;
using Shared.Contracts.Events.Payment;
using Shared.Contracts.Events.Stock;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Payment.NetFramework481.Application.Payments.Sagas.Handlers;

public sealed class ProcessPaymentSagaHandler(
    IPaymentRepository paymentRepository,
    ILogger<ProcessPaymentSagaHandler> logger)
: ReactiveSagaHandler<StockReservedEvent>
, ISagaCompensationHandler<ShipmentScheduledFailedEvent>
{
    public override async Task HandleAsync(StockReservedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            //throw new Exception("GOKCE");

            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Processing payment for order: {OrderId}", message.OrderId);

            var payment = new Domain.Payments.Payment
            {
                TransactionId = System.Guid.NewGuid(),
                OrderId = message.OrderId,
                Amount = message.TotalAmount,
                SaveCardId = message.SavedCardId,
                Status = PaymentStatus.Pending
            };

            await paymentRepository.SaveAsync(payment, cancellationToken);
            logger.LogInformation("Payment created: {PaymentId}", payment.Id);

            logger.LogInformation("Processing payment with card: {CardHolderName}, Last4: {Last4}, Expiry: {Month}/{Year}",
                message.CardHolderName, message.CardLast4Digits, message.CardExpiryMonth, message.CardExpiryYear);

            var success = await paymentRepository.ProcessPaymentAsync(
                payment.Id,
                message.CardHolderName,
                message.CardLast4Digits,
                message.CardExpiryMonth,
                message.CardExpiryYear,
                cancellationToken);
            if (success)
            {
                var paymentProcessedEvent = new PaymentProcessedEvent
                {
                    OrderId = message.OrderId,
                    PaymentId = payment.Id,
                    TransactionId = payment.TransactionId.ToString(),
                    Amount = payment.Amount,
                    CustomerName = message.CustomerName,
                    CustomerEmail = message.CustomerEmail,
                    CustomerPhone = message.CustomerPhone,
                    ShippingStreet = message.ShippingStreet,
                    ShippingCity = message.ShippingCity,
                    ShippingState = message.ShippingState,
                    ShippingZipCode = message.ShippingZipCode,
                    ShippingCountry = message.ShippingCountry
                };
                await Context.Publish(paymentProcessedEvent, cancellationToken);

                await Context.MarkAsComplete<StockReservedEvent>();

                logger.LogInformation("Payment processed successfully for order: {OrderId}", message.OrderId);
            }
            else
            {
                logger.LogError("Payment processing failed for order: {OrderId}. Reason: {FailureReason}", message.OrderId, payment.FailureReason);
                throw new Exception(payment.FailureReason);
            }
        }
        catch (OperationCanceledException ex)
        {
            await Context.Publish(new PaymentProcessedFailedEvent(ex.Message) { OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsCancelled<StockReservedEvent>(ex);
        }
        catch (Exception ex)
        {
            await Context.Publish(new PaymentProcessedFailedEvent(ex.Message) { OrderId = message.OrderId }, cancellationToken);
            await Context.MarkAsFailed<StockReservedEvent>(ex, cancellationToken);
        }
    }

    public async Task CompensateAsync(ShipmentScheduledFailedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await paymentRepository.GetByOrderIdAsync(message.OrderId, cancellationToken);
            if (payment != null && payment.Status == PaymentStatus.Completed)
                await paymentRepository.UpdateStatusAsync(payment.Id, PaymentStatus.Refunded, cancellationToken);

            await Context.MarkAsCompensated<ShipmentScheduledFailedEvent>();
        }
        catch (Exception ex)
        {
            await Context.MarkAsCompensationFailed<ShipmentScheduledFailedEvent>(ex);
        }
    }
}
