using Lycia.Handlers;
using Microsoft.Extensions.Logging;
using Sample_Net21.Shared.Messages.Commands;
using Sample_Net21.Shared.Messages.Events;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample_Core31.Order.Choreography.Api.Sagas
{
    public sealed class CreateOrderSagaHandler : StartReactiveSagaHandler<CreateOrderCommand>
    {
        public ILogger<CreateOrderSagaHandler> logger { get; }
        public CreateOrderSagaHandler(ILogger<CreateOrderSagaHandler> _logger)
        {
            logger = _logger;
        }

        public override async Task HandleStartAsync(CreateOrderCommand createOrderCommand, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (createOrderCommand == null)
            {
                logger.LogError("CreateOrderCommand is null.");
                throw new ArgumentNullException(nameof(createOrderCommand));
            }

            //Insert into db

            var orderCreatedEvent = OrderCreatedEvent.Create
            (
                Guid.NewGuid(),
                createOrderCommand.CustomerId,
                createOrderCommand.ShippingAddress,
                createOrderCommand.OrderTotal,
                createOrderCommand.Items
            );

            logger.LogInformation("CreateOrderCommand Completed for OrderId: {OrderId}", createOrderCommand.OrderId);
            await Context.PublishWithTracking(orderCreatedEvent).ThenMarkAsComplete();
        }

        public override async Task CompensateStartAsync(CreateOrderCommand message, CancellationToken cancellationToken = default)
        {
            //ORDER
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogInformation("CreateOrderCommand Compensated for OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensated<CreateOrderCommand>();
            }
            catch (Exception ex)
            {
                logger.LogInformation("CreateOrderCommand Compensation Failed for OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensationFailed<CreateOrderCommand>();
            }
        }
    }

}