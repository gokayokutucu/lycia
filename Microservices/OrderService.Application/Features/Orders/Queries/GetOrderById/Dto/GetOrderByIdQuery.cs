using MediatR;

namespace OrderService.Application.Features.Orders.Queries;

public sealed record GetOrderByIdQuery(Guid OrderId) : IRequest<GetOrderByIdQueryResult>
{
    public static GetOrderByIdQuery Create(Guid orderId) => new GetOrderByIdQuery(orderId);
}
