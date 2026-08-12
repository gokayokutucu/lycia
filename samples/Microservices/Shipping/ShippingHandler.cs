using Lycia.Saga.Messaging.Handlers; using Lycia.Samples.Microservices.Contracts;
namespace Lycia.Samples.Microservices.Shipping;
public sealed class ShippingHandler:CoordinatedResponsiveSagaHandler<ShipOrderCommand,OrderShippedResponse,ServiceSagaData>
{ public override async Task HandleAsync(ShipOrderCommand message,CancellationToken token=default){Context.Data.OrderId=message.OrderId; await Context.RespondWithTracking(message,new OrderShippedResponse{OrderId=message.OrderId},token).ThenMarkAsComplete<ShipOrderCommand>(token);} }
