using MediatR;
using OrderService.Application.Contracts.Persistence;

namespace OrderService.Application.Features.Orders.Commands;

public sealed record DeleteOrderCommandHandler(IOrderRepository orderRepository)
    : IRequestHandler<DeleteOrderCommand, DeleteOrderCommandResult>
{
    public async Task<DeleteOrderCommandResult> Handle(DeleteOrderCommand request, CancellationToken cancellationToken)
    {
        var success = await orderRepository.DeleteAsync(request.OrderId, cancellationToken);
        return DeleteOrderCommandResult.Create(success);
    }
}