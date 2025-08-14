using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Infrastructure.Repositories;

public sealed class StockRepository(ILogger<StockRepository> logger) 
    : IStockRepository
{
    public async Task<bool> IsReservedAsync(Guid reservationId)
    {
        return true;
    }

    public async Task<bool> IsStockAvailableAsync(Guid productId, int quantity)
    {
        return true;
    }

    public async Task ReleaseStockAsync(Guid reservationId)
    {
        
    }

    public async Task ReserveStockAsync(Stock stock, Guid orderId, DateTime until)
    {
        
    }
}
