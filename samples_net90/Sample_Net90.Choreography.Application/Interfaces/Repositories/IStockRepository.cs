namespace Sample_Net90.Choreography.Application.Interfaces.Repositories;

public interface IStockRepository
{
    Task<bool> IsStockAvailableAsync(Guid productId, int quantity);
    Task ReleaseStockAsync(Guid orderId, Guid productId, int quantity);
    Task ReserveStockAsync(Guid orderId, Guid productId, int quantity, int minutes = 15);
}
