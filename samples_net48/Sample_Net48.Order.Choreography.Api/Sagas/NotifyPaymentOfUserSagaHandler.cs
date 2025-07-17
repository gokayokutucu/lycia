using Lycia.Saga.Handlers;
using Microsoft.Extensions.Logging;
using Sample_Net48.Shared.Messages.Events;
using System;
using System.Threading.Tasks;

namespace Sample_Net48.Order.Choreography.Api.Sagas
{
    public class NotifyPaymentOfUserSagaHandler : ReactiveSagaHandler<PaymentProcessedEvent>
    {
        private readonly ILogger<NotifyPaymentOfUserSagaHandler> logger;
        public NotifyPaymentOfUserSagaHandler(ILogger<NotifyPaymentOfUserSagaHandler> _logger)
        {
            logger = _logger;
        }

        public override async Task HandleAsync(PaymentProcessedEvent paymentProcessedEvent)
        {
            if (paymentProcessedEvent == null)
            {
                logger.LogError("PaymentProcessedEvent is null.");
                throw new ArgumentNullException(nameof(paymentProcessedEvent));
            }

            //Insert into db

            logger.LogInformation("Send purchased product notification for OrderId: {OrderId}", paymentProcessedEvent.OrderId);
            await Context.MarkAsComplete<PaymentProcessedEvent>();
        }

        public override async Task CompensateAsync(PaymentProcessedEvent message)
        {
            try
            {
                logger.LogInformation("Compensating for failed payment notification. OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensated<PaymentProcessedEvent>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Compensation failed");
                await Context.MarkAsCompensationFailed<PaymentProcessedEvent>();
            }
        }
    }
}