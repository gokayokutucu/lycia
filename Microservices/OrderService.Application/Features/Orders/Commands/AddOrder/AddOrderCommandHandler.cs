using MediatR;
using OrderService.Application.Contracts.Persistence;
using OrderService.Domain.Entities;

namespace OrderService.Application.Features.Orders.Commands;

public sealed record AddOrderCommandHandler(IOrderRepository orderRepository) 
    : IRequestHandler<AddOrderCommand, AddOrderCommandResult>
{
    public async Task<AddOrderCommandResult> Handle(AddOrderCommand request, CancellationToken cancellationToken)
    {
        var order = Order.Create(Guid.Empty, request.CustomerId, request.OrderDate, request.OrderItems);
        var success = await orderRepository.AddAsync(order, cancellationToken);
        return AddOrderCommandResult.Create(success);
    }
}
