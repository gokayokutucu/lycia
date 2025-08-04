namespace Sample_Net90.Choreography.Application.Interfaces.Repositories;

public interface IStockRepository
{
    Task<bool> IsReserved(Guid reservationId);
    Task<bool> IsStockAvailableAsync(Guid productId, int quantity);
    Task ReleaseStockAsync(Guid reservationId);
    Task ReserveStockAsync(Domain.Entities.Stock stock, DateTime until);
}
