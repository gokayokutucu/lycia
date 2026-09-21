using Lycia.Saga.Messaging.Handlers; using Lycia.Samples.Microservices.Contracts;
namespace Lycia.Samples.Microservices.Order;
public sealed class OrderHandler:CoordinatedResponsiveSagaHandler<CreateOrderCommand,OrderCreatedResponse,ServiceSagaData>
{ public override async Task HandleAsync(CreateOrderCommand message,CancellationToken token=default){Context.Data.OrderId=message.OrderId; await Context.RespondWithTracking(message,new OrderCreatedResponse{OrderId=message.OrderId},token).ThenMarkAsComplete<CreateOrderCommand>(token);} }
