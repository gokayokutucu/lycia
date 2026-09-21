using Lycia.Common.Messaging;
using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Messaging.Handlers;
using Lycia.Samples.Microservices.Contracts;

namespace Lycia.Samples.Microservices.Checkout;

public sealed class CheckoutSagaHandler : StartCoordinatedSagaHandler<StartCheckoutCommand, CheckoutSagaData>,
    IResponseSagaHandler<OrderCreatedResponse>, IResponseSagaHandler<InventoryReservedResponse>,
    IResponseSagaHandler<PaymentSucceededResponse>, IResponseSagaHandler<OrderShippedResponse>
{
    public override async Task HandleStartAsync(StartCheckoutCommand message, CancellationToken token = default)
    {
        Context.Data.OrderId = message.OrderId; Context.Data.Status = "CreatingOrder";
        Context.Data.FailAt = message.FailAt;
        await Context.SendWithTracking(new CreateOrderCommand { OrderId = message.OrderId }, token)
            .ThenMarkAsComplete<StartCheckoutCommand>(token);
    }
    public async Task HandleSuccessResponseAsync(OrderCreatedResponse response, CancellationToken token = default)
    {
        Context.Data.Status="ReservingInventory";
        await Context.SendWithTracking(new ReserveInventoryCommand
        {
            OrderId=response.OrderId,
            InjectFailure=string.Equals(Context.Data.FailAt,"inventory",StringComparison.OrdinalIgnoreCase)
        },token).ThenMarkAsComplete<OrderCreatedResponse>(token);
    }
    public Task HandleFailResponseAsync(OrderCreatedResponse response, FailResponse fail, CancellationToken token=default)=>Fail<OrderCreatedResponse>(token);
    public async Task HandleSuccessResponseAsync(InventoryReservedResponse response,CancellationToken token=default)
    {
        Context.Data.Status="ProcessingPayment";
        await Context.SendWithTracking(new ProcessPaymentCommand
        {
            OrderId=response.OrderId,
            InjectFailure=string.Equals(Context.Data.FailAt,"payment",StringComparison.OrdinalIgnoreCase)
        },token).ThenMarkAsComplete<InventoryReservedResponse>(token);
    }
    public Task HandleFailResponseAsync(InventoryReservedResponse response,FailResponse fail,CancellationToken token=default)=>Fail<InventoryReservedResponse>(token);
    public async Task HandleSuccessResponseAsync(PaymentSucceededResponse response,CancellationToken token=default)
    { Context.Data.Status="Shipping"; await Context.SendWithTracking(new ShipOrderCommand { OrderId=response.OrderId },token).ThenMarkAsComplete<PaymentSucceededResponse>(token); }
    public Task HandleFailResponseAsync(PaymentSucceededResponse response,FailResponse fail,CancellationToken token=default)=>Fail<PaymentSucceededResponse>(token);
    public async Task HandleSuccessResponseAsync(OrderShippedResponse response,CancellationToken token=default)
    { Context.Data.Status="Completed"; Context.Data.IsCompleted=true; await Context.Publish(new CheckoutCompletedEvent { OrderId=response.OrderId },token); await Context.MarkAsComplete<OrderShippedResponse>(token); }
    public Task HandleFailResponseAsync(OrderShippedResponse response,FailResponse fail,CancellationToken token=default)=>Fail<OrderShippedResponse>(token);
    private Task Fail<T>(CancellationToken token) where T:Lycia.Saga.Abstractions.Messaging.IMessage { Context.Data.Status="Failed"; return Context.MarkAsFailed<T>(token); }
}
