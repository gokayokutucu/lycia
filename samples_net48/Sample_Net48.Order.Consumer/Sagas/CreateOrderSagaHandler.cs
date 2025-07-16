using Lycia.Saga.Handlers;
using Sample_Net48.Shared.Messages.Commands;
using Sample_Net48.Shared.Messages.Events;
using System;
using System.Runtime.Remoting.Contexts;
using System.Threading.Tasks;

namespace Sample_Net48.Order.Consumer.Sagas
{
    /// <summary>
    /// Handles the start of the order process in a reactive saga flow and emits an OrderCreatedEvent.
    /// </summary>
    public class CreateOrderSagaHandler :
        StartReactiveSagaHandler<CreateOrderCommand>
    {
        /// <summary>
        /// For test purposes, we can check if the compensation was called.
        /// </summary>
        public bool CompensateCalled { get; private set; }

        public override async Task HandleStartAsync(CreateOrderCommand command)
        {
            // Publish the success response event
            // await Context
            //     .PublishWithTracking(new OrderCreatedEvent
            //     {
            //         OrderId = command.OrderId,
            //         UserId = command.UserId,
            //         TotalPrice = command.TotalPrice
            //     }).ThenMarkAsComplete();

            // await Context.Publish(new OrderCreatedEvent
            // {
            //     OrderId = command.OrderId,
            //     UserId = command.UserId,
            //     TotalPrice = command.TotalPrice
            // });
            //
            await Context.MarkAsComplete<CreateOrderCommand>();
        }

        public override async Task CompensateStartAsync(CreateOrderCommand message)
        {
            try
            {
                CompensateCalled = true;
                // Compensation logic
                await Context.MarkAsCompensated<CreateOrderCommand>();
            }
            catch (Exception ex)
            {
                // Log, notify, halt chain, etc.
                Console.WriteLine($"❌ Compensation failed: {ex.Message}");

                await Context.MarkAsCompensationFailed<CreateOrderCommand>();
                // Optionally: rethrow or store for manual retry
                throw; // Or suppress and log for retry system
            }
        }
    }
}