using Lycia.Saga.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

/// <summary>
/// Handles the start of the order process in a reactive saga flow and emits an OrderCreatedEvent.
/// </summary>
public class CreateOrderSagaHandler :
    StartCoordinatedSagaHandler<CreateOrderCommand, CreateOrderSagaData>
{
    /// <summary>
    /// For test purposes, we can check if the compensation was called.
    /// </summary>
    public bool CompensateCalled { get; private set; }

    public override async Task HandleStartAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        // Publish the success response event
        await Context
            .PublishWithTracking(new OrderCreatedEvent
            {
                OrderId = command.OrderId,
                UserId = command.UserId,
                TotalPrice = command.TotalPrice
            }, cancellationToken).ThenMarkAsComplete(cancellationToken);

        #region Other way to publish an event

        // await Context.Publish(new OrderCreatedEvent
        // {
        //     OrderId = command.OrderId,
        //     UserId = command.UserId,
        //     TotalPrice = command.TotalPrice
        // });
        //
        //await Context.MarkAsComplete<CreateOrderCommand>();

        #endregion
    }

    public override async Task CompensateStartAsync(CreateOrderCommand message, CancellationToken cancellationToken = default)
    {
        try
        {
            CompensateCalled = true;
            // Compensation logic
            await Context.CompensateAndBubbleUp<CreateOrderCommand>(cancellationToken);
        }
        catch (Exception ex)
        {
            // Log, notify, halt chain, etc.
            Console.WriteLine($"❌ Compensation failed: {ex.Message}");

            await Context.MarkAsCompensationFailed<CreateOrderCommand>(ex, cancellationToken);
            // Optionally: rethrow or store for manual retry
            throw; // Or suppress and log for retry system
        }
    }
}