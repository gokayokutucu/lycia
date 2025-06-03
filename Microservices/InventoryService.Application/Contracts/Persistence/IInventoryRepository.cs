using InventoryService.Domain.Entities;

namespace InventoryService.Application.Contracts.Persistence;

public interface IInventoryRepository
{
    Task<bool> AddAsync(Stock Stock, CancellationToken cancellationToken = default);
    Task<IEnumerable<Stock>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Stock> GetByIdAsync(Guid StockId, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Stock Stock, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid StockId, CancellationToken cancellationToken = default);
}
