namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class CreateOrderCommandResult
{
    public Guid OrderId { get; init; }
    public static CreateOrderCommandResult Create(Guid orderId) 
        => new CreateOrderCommandResult
    {
        OrderId = orderId
    };
}
