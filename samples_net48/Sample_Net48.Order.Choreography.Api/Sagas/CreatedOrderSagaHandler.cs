using Lycia.Saga.Handlers;
using Microsoft.Extensions.Logging;
using Sample_Net48.Shared.Messages.Commands;
using Sample_Net48.Shared.Messages.Events;
using System;
using System.Threading.Tasks;

namespace Sample_Net48.Order.Choreography.Api.Sagas
{
    public sealed class CreateOrderSagaHandler : StartReactiveSagaHandler<CreateOrderCommand>
    {
        public ILogger<CreateOrderSagaHandler> logger { get; }
        public CreateOrderSagaHandler(ILogger<CreateOrderSagaHandler> _logger)
        {
            logger = _logger;
        }

        public override async Task HandleStartAsync(CreateOrderCommand createOrderCommand)
        {
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

            logger.LogInformation("Created order for OrderId: {OrderId}", createOrderCommand.OrderId);
            await Context.PublishWithTracking(orderCreatedEvent).ThenMarkAsComplete();
        }

        public override async Task CompensateStartAsync(CreateOrderCommand message)
        {
            try
            {
                logger.LogInformation("Compensating for failed order creation. OrderId: {OrderId}", message.OrderId);
                await Context.MarkAsCompensated<CreateOrderCommand>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Compensation failed");
                await Context.MarkAsCompensationFailed<CreateOrderCommand>();
            }
        }
    }

}