using OrderService.Domain.Entities;

namespace OrderService.Application.Contracts.Persistence
{
    public interface IOrderRepository
    {
        Task<bool> AddAsync(Order order, CancellationToken cancellationToken = default);
        Task<IEnumerable<Order>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<Order> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(Order order, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(Guid orderId, CancellationToken cancellationToken = default);
    }
}
