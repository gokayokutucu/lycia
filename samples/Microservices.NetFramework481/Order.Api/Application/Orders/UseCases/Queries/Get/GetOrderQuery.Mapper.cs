using Mapster;
using Shared.Contracts.Dtos;
using System.Linq;

namespace Sample.Order.NetFramework481.Application.Orders.UseCases.Queries.Get;

/// <summary>
/// Mapster configuration for GetOrderQuery use case.
/// </summary>
public static class GetOrderQueryMapper
{
    static GetOrderQueryMapper()
    {
        TypeAdapterConfig<Domain.Orders.Order, GetOrderQueryResult>
            .NewConfig()
            .Map(dest => dest.OrderId, src => src.Id)
            .Map(dest => dest.Items, src => src.Items.Select(i => new OrderItemDto
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList());
    }
}
