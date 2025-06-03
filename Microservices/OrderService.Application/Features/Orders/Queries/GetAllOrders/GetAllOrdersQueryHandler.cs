using MediatR;
using OrderService.Application.Contracts.Persistence;

namespace OrderService.Application.Features.Orders.Queries;

public sealed class GetAllOrdersQueryHandler(IOrderRepository orderRepository) 
    : IRequestHandler<GetAllOrdersQuery, GetAllOrdersQueryResult>
{
    public async Task<GetAllOrdersQueryResult> Handle(GetAllOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await orderRepository.GetAllAsync(cancellationToken);
        return GetAllOrdersQueryResult.Create(orders);
    }
}