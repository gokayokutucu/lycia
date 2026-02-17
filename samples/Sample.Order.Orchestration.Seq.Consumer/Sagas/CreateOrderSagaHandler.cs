// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Seq.Consumer.Sagas;

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
        await Context.SendWithTracking(new ReserveInventoryCommand
        {
            OrderId = command.OrderId,
        }, cancellationToken)
        .ThenMarkAsComplete();
    }

    public override async Task CompensateStartAsync(CreateOrderCommand message, CancellationToken cancellationToken = default)
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
            
            await Context.MarkAsCompensationFailed<CreateOrderCommand>(ex);
            // Optionally: rethrow or store for manual retry
            throw; // Or suppress and log for retry system
        }
    }
}