
using MediatR;
using OrderService.Domain.Entities;

namespace OrderService.Application.Features.Orders.Queries;

public sealed record GetAllOrdersQuery() : IRequest<GetAllOrdersQueryResult>
{
    public static GetAllOrdersQuery Create() => new GetAllOrdersQuery();
}
