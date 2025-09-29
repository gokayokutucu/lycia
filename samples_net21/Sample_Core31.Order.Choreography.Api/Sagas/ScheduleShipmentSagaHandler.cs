using Lycia.Handlers;
using Microsoft.Extensions.Logging;
using Sample_Net21.Shared.Messages.Events;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample_Core31.Order.Choreography.Api.Sagas
{
    public sealed class ScheduleShipmentSagaHandler : ReactiveSagaHandler<PaymentProcessedEvent>
    {
        private readonly ILogger<ScheduleShipmentSagaHandler> logger;
        public ScheduleShipmentSagaHandler(ILogger<ScheduleShipmentSagaHandler> _logger)
        {
            logger = _logger;
        }

        public override async Task HandleAsync(PaymentProcessedEvent paymentProcessedEvent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (paymentProcessedEvent == null)
            {
                logger.LogError("PaymentProcessedEvent is null.");
                throw new ArgumentNullException(nameof(paymentProcessedEvent));
            }

            //Insert into db

            logger.LogInformation("ScheduleShipment Completed for OrderId: {OrderId}", paymentProcessedEvent.OrderId);
            await Context.MarkAsComplete<PaymentProcessedEvent>();
        }

        public override async Task CompensateAsync(PaymentProcessedEvent message, CancellationToken cancellationToken = default)
        {
            //SHIPMENT
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogInformation("ScheduleShipment Compensated for OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensated<PaymentProcessedEvent>();
            }
            catch (Exception ex)
            {
                logger.LogInformation("ScheduleShipment Compensation Failed for OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensationFailed<PaymentProcessedEvent>();
            }
        }
    }
}