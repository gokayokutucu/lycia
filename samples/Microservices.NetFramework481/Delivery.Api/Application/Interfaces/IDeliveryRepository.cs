using System;
using System.Threading;
using System.Threading.Tasks;
using Sample.Delivery.NetFramework481.Domain.Deliveries;

namespace Sample.Delivery.NetFramework481.Application.Interfaces;

/// <summary>
/// Repository interface for Delivery aggregate.
/// </summary>
public interface IDeliveryRepository
{
    Task<Domain.Deliveries.Delivery?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SaveAsync(Domain.Deliveries.Delivery delivery, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid deliveryId, DeliveryStatus status, CancellationToken cancellationToken = default);
}
