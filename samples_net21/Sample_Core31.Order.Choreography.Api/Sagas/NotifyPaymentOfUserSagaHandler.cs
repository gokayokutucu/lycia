using Lycia.Handlers;
using Microsoft.Extensions.Logging;
using Sample_Net21.Shared.Messages.Events;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample_Core31.Order.Choreography.Api.Sagas
{
    public class NotifyPaymentOfUserSagaHandler : ReactiveSagaHandler<PaymentProcessedEvent>
    {
        private readonly ILogger<NotifyPaymentOfUserSagaHandler> logger;
        public NotifyPaymentOfUserSagaHandler(ILogger<NotifyPaymentOfUserSagaHandler> _logger)
        {
            logger = _logger;
        }

        public override async Task HandleAsync(PaymentProcessedEvent paymentProcessedEvent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            //throw new NotImplementedException("This handler is not implemented yet. Please implement the logic to notify the user of payment processing.");
            if (paymentProcessedEvent == null)
            {
                logger.LogError("PaymentProcessedEvent is null.");
                throw new ArgumentNullException(nameof(paymentProcessedEvent));
            }

            //Insert into db

            logger.LogInformation("SendNotification Completed for OrderId: {OrderId}", paymentProcessedEvent.OrderId);
            await Context.MarkAsComplete<PaymentProcessedEvent>();
        }

        public override async Task CompensateAsync(PaymentProcessedEvent message, CancellationToken cancellationToken = default)
        {
            //NOTIFY
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogInformation("SendNotification Compensated for OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensated<PaymentProcessedEvent>();
            }
            catch (Exception ex)
            {
                logger.LogInformation("SendNotification Compensation Failed for OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensationFailed<PaymentProcessedEvent>();
            }
        }
    }
}