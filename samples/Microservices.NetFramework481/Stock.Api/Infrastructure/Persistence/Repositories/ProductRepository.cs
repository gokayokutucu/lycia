using Microsoft.EntityFrameworkCore;
using Sample.Product.NetFramework481.Application.Interfaces;
using Shared.Contracts.Dtos;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Product.NetFramework481.Infrastructure.Persistence.Repositories;

public sealed class ProductRepository(ProductDbContext context) : IProductRepository
{
    public async Task<Domain.Products.Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default)
        => await context.Products.FindAsync([productId], cancellationToken);

    public async Task ReserveStockAsync(Guid orderId, List<OrderItemDto> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            var product = await context.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken);

            if (product == null)
                throw new InvalidOperationException($"Product not found: {item.ProductName}");

            if (product.AvailableQuantity < item.Quantity)
                throw new InvalidOperationException($"Insufficient stock for {item.ProductName}. Available: {product.AvailableQuantity}, Requested: {item.Quantity}");

            product.ReservedQuantity += item.Quantity;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseStockAsync(Guid orderId, List<OrderItemDto> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            var product = await context.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken);

            if (product != null)
                product.ReservedQuantity = Math.Max(0, product.ReservedQuantity - item.Quantity);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}

