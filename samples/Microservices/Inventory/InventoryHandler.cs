using Lycia.Saga.Messaging.Handlers; using Lycia.Samples.Microservices.Contracts;
namespace Lycia.Samples.Microservices.Inventory;
public sealed class InventoryHandler:CoordinatedResponsiveSagaHandler<ReserveInventoryCommand,InventoryReservedResponse,ServiceSagaData>
{
    public override async Task HandleAsync(ReserveInventoryCommand message,CancellationToken token=default)
    {
        if (message.InjectFailure)
            throw new InvalidOperationException("Injected inventory failure.");
        Context.Data.OrderId=message.OrderId;
        await Context.RespondWithTracking(message,new InventoryReservedResponse{OrderId=message.OrderId},token)
            .ThenMarkAsComplete<ReserveInventoryCommand>(token);
    }
}
