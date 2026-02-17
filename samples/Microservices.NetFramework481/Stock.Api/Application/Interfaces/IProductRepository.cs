using Shared.Contracts.Dtos;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Product.NetFramework481.Application.Interfaces;

public interface IProductRepository
{
    Task<Domain.Products.Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task ReserveStockAsync(Guid orderId, List<OrderItemDto> items, CancellationToken cancellationToken);
    Task ReleaseStockAsync(Guid orderId, List<OrderItemDto> items, CancellationToken cancellationToken);
}
