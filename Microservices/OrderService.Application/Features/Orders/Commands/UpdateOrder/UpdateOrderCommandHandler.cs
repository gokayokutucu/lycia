using MediatR;
using OrderService.Application.Contracts.Persistence;
using OrderService.Domain.Entities;

namespace OrderService.Application.Features.Orders.Commands;

public sealed record UpdateOrderCommandHandler(IOrderRepository orderRepository)
    : IRequestHandler<UpdateOrderCommand, UpdateOrderCommandResult>
{
    public async Task<UpdateOrderCommandResult> Handle(UpdateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = Order.Create(request.OrderId, request.CustomerId, request.OrderDate, request.OrderItems);
        var success = await orderRepository.UpdateAsync(order, cancellationToken);
        return UpdateOrderCommandResult.Create(success);
    }
}
