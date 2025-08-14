namespace Sample_Net90.Choreography.Application.Interfaces.Repositories;

public interface IStockRepository
{
    Task<bool> IsReservedAsync(Guid reservationId);
    Task<bool> IsStockAvailableAsync(Guid productId, int quantity);
    Task ReleaseStockAsync(Guid reservationId);
    Task ReserveStockAsync(Domain.Entities.Stock stock, Guid orderId, DateTime until);
}
