using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Order.NetFramework481.Application.Interfaces;

public interface IOrderRepository
{
    Task<Domain.Orders.Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveAsync(Domain.Orders.Order order, CancellationToken cancellationToken = default);

    Task UpdateAsync(Domain.Orders.Order order, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}
