using Lycia.Saga.Handlers;
using Microsoft.Extensions.Logging;
using Sample_Net21.Shared.Messages.Events;
using System;
using System.Threading.Tasks;

namespace Sample_Core31.Order.Consumer.Sagas
{
    public sealed class ScheduleShipmentSagaHandler : ReactiveSagaHandler<PaymentProcessedEvent>
    {
        private readonly ILogger<ScheduleShipmentSagaHandler> logger;
        public ScheduleShipmentSagaHandler(ILogger<ScheduleShipmentSagaHandler> _logger)
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

            logger.LogInformation("Scheduling shipment for OrderId: {OrderId}", paymentProcessedEvent.OrderId);
            await Context.MarkAsComplete<PaymentProcessedEvent>();
        }

        public override async Task CompensateAsync(PaymentProcessedEvent message)
        {
            try
            {
                logger.LogInformation("Compensating for failed shipment schedule. OrderId: {OrderId}", message.OrderId);
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