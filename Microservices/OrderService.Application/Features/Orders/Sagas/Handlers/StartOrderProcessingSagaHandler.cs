using Lycia.Saga.Handlers;
using OrderService.Application.Contracts.Infrastructure; // For IMessageBroker (to be created)
using OrderService.Application.Features.Orders.Sagas.Commands;
using OrderService.Application.Features.Orders.Sagas.Data;
using OrderService.Application.Features.Orders.Sagas.Events;

namespace OrderService.Application.Features.Orders.Sagas.Handlers
{
    public class StartOrderProcessingSagaHandler : StartReactiveSagaHandler<StartOrderProcessingSagaCommand, OrderProcessingSagaData>
    {
        private readonly IMessageBroker _messageBroker; // To be defined and implemented later
        private readonly OrderProcessingSagaData SagaData = new(); // Assuming this is the SagaData class for this saga

        public StartOrderProcessingSagaHandler(IMessageBroker messageBroker)
        {
            _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        }

        public override async Task HandleStartAsync(StartOrderProcessingSagaCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            // Initialize SagaData from the command
            SagaData.Id = command.SagaId; // SagaData.Id is the SagaInstanceId, which we set in StartOrderProcessingSagaCommand
            SagaData.OrderId = command.OrderId;
            SagaData.UserId = command.UserId;
            SagaData.TotalPrice = command.TotalPrice;
            SagaData.Items = command.Items; // Assuming OrderItemSagaDto is used in both
            SagaData.OrderStatus = "OrderProcessingStarted";

            // Create the event to publish to RabbitMQ
            var eventToPublish = new OrderCreationConfirmedEvent(
                SagaData.OrderId,
                SagaData.UserId,
                SagaData.TotalPrice,
                SagaData.Items,
                DateTime.UtcNow
            );

            try
            {
                // Publish this event to RabbitMQ using the injected message broker
                // The actual topic/exchange would be configured in the IMessageBroker implementation
                await _messageBroker.PublishAsync(eventToPublish);

                SagaData.OrderStatus = "OrderConfirmationPublished";

                // Mark the saga step as complete
                // For a StartReactiveSagaHandler, this indicates the saga has successfully started
                // and its initial state is saved.
                await MarkAsComplete();
            }
            catch (Exception ex)
            {
                // If publishing to message broker fails, we need to decide how to handle this.
                // For a StartSaga handler, this is critical.
                // We could set a failed status and publish a local domain event for failure,
                // or let the exception propagate to be caught by a global error handler for sagas.
                SagaData.OrderStatus = "OrderConfirmationPublishFailed";
                // Optionally, rethrow or publish a local "saga failed" event via MediatR
                // For now, just log and rethrow to make it visible this critical step failed.
                Console.WriteLine($"Failed to publish OrderCreationConfirmedEvent for SagaId {SagaData.Id}. Error: {ex.Message}"); // Replace with proper logging

                // To ensure the saga doesn't get stuck in an "started but failed immediately" state without record:
                // Option 1: Rethrow, let higher level saga error handling deal with it.
                // Option 2: Publish a specific failure event for this saga instance if Lycia supports it easily here.
                // Option 3: Use Context.MarkAsFaulted() if appropriate, but this handler is for starting.
                // For now, rethrowing is a simple way to indicate failure of this handler.
                throw;
            }
        }
    }
}
