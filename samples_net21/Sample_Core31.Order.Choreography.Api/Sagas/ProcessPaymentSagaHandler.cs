using Lycia.Abstractions;
using Lycia.Handlers;
using Microsoft.Extensions.Logging;
using Sample_Net21.Shared.Messages.Events;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sample_Core31.Order.Choreography.Api.Sagas
{
    public sealed class ProcessPaymentSagaHandler : ReactiveSagaHandler<StockReservedEvent>
    {
        private readonly ILogger<ProcessPaymentSagaHandler> logger;
        public ProcessPaymentSagaHandler(ILogger<ProcessPaymentSagaHandler> _logger)
        {
            logger = _logger;
        }

        public override async Task HandleAsync(StockReservedEvent stockReservedEvent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stockReservedEvent == null)
            {
                logger.LogError("StockReservedEvent is null.");
                throw new ArgumentNullException(nameof(stockReservedEvent));
            }

            //Insert into db

            var paymentProcessedEvent = PaymentProcessedEvent.Create
            (
                stockReservedEvent.OrderId,
                stockReservedEvent.CustomerId,
                stockReservedEvent.Items?.Sum(item => item.Price * item.Quantity) ?? 0
            );

            logger.LogInformation("ProcessPayment Completed for OrderId: {OrderId}", stockReservedEvent.OrderId);
            await Context.PublishWithTracking(paymentProcessedEvent).ThenMarkAsComplete();
        }

        public override async Task CompensateAsync(StockReservedEvent message, CancellationToken cancellationToken = default)
        {
            //PAYMENT
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogInformation("ProcessPayment Compensated for OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensated<StockReservedEvent>();
            }
            catch (Exception ex)
            {
                logger.LogInformation("ProcessPayment Compensation Failed for OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensationFailed<StockReservedEvent>();
            }
        }
    }
}