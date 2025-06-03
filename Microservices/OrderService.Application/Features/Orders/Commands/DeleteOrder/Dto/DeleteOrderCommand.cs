using MediatR;

namespace OrderService.Application.Features.Orders.Commands;

public sealed record DeleteOrderCommand: IRequest<DeleteOrderCommandResult>
{
    public Guid OrderId { get; init; }
    public static DeleteOrderCommand Create(Guid orderId) 
        => new DeleteOrderCommand
        {
            OrderId = orderId
        };
}
