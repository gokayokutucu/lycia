using Microsoft.EntityFrameworkCore;
using Sample.Delivery.NetFramework481.Application.Interfaces;
using Sample.Delivery.NetFramework481.Domain.Deliveries;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Delivery.NetFramework481.Infrastructure.Persistence.Repositories;

public sealed class DeliveryRepository(DeliveryDbContext context) : IDeliveryRepository
{
    public async Task<Domain.Deliveries.Delivery?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await context.Deliveries.FirstOrDefaultAsync(d => d.OrderId == orderId, cancellationToken);

    public async Task SaveAsync(Domain.Deliveries.Delivery delivery, CancellationToken cancellationToken = default)
    {
        await context.Deliveries.AddAsync(delivery, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(Guid deliveryId, DeliveryStatus status, CancellationToken cancellationToken = default)
    {
        var delivery = await context.Deliveries.FindAsync([deliveryId], cancellationToken);
        if (delivery == null)
            throw new InvalidOperationException($"Delivery not found: {deliveryId}");

        context.Entry(delivery).Property(nameof(Domain.Deliveries.Delivery.Status)).CurrentValue = status;

        if (status == DeliveryStatus.Delivered)
            context.Entry(delivery).Property(nameof(Domain.Deliveries.Delivery.DeliveryDate)).CurrentValue = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
    }
}
