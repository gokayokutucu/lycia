namespace Sample_Net90.Choreography.Application.Interfaces.Repositories;

public interface IOrderRepository
{
    Task<Guid> CreateAsync(Domain.Entities.Order order);
    Task DeleteAsync(Domain.Entities.Order order);
    Task<bool> OrderExistsAsync(Guid orderId);
}
