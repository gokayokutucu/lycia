using System;
using System.Threading;
using System.Threading.Tasks;
using OrderService.Domain.Aggregates.Order; // Assuming Order aggregate is here

namespace OrderService.Application.Contracts.Persistence
{
    public interface IOrderRepository
    {
        Task AddAsync(Order order, CancellationToken cancellationToken = default);
        Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        // Potentially:
        // Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
        // Task DeleteAsync(Order order, CancellationToken cancellationToken = default);
    }
}
