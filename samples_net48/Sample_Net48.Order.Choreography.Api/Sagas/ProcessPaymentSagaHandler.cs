using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers;
using Microsoft.Extensions.Logging;
using Sample_Net48.Shared.Messages.Events;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sample_Net48.Order.Choreography.Api.Sagas
{
    public sealed class ProcessPaymentSagaHandler : ReactiveSagaHandler<StockReservedEvent>
    {
        private readonly ILogger<ProcessPaymentSagaHandler> logger;
        public ProcessPaymentSagaHandler(ILogger<ProcessPaymentSagaHandler> _logger)
        {
            logger = _logger;
        }

        public override async Task HandleAsync(StockReservedEvent stockReservedEvent)
        {
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

            logger.LogInformation("Processed payment for OrderId: {OrderId}", stockReservedEvent.OrderId);
            await Context.PublishWithTracking(paymentProcessedEvent).ThenMarkAsComplete();
        }

        public override async Task CompensateAsync(StockReservedEvent message)
        {
            try
            {
                logger.LogInformation("Compensating for failed payment process. OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensated<StockReservedEvent>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Compensation failed");
                await Context.MarkAsCompensationFailed<StockReservedEvent>();
            }
        }
    }
}