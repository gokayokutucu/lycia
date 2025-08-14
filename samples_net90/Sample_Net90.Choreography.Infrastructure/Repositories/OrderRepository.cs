using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Infrastructure.Repositories;

public sealed class OrderRepository(ILogger<OrderRepository> logger)
    : IOrderRepository
{
    public async Task<Guid> CreateAsync(Order order)
    {
        return Guid.CreateVersion7();
    }

    public async Task DeleteAsync(Order order)
    {

        //throw new NotImplementedException();
    }

    public async Task<bool> OrderExistsAsync(Guid orderId)
    {
        return true;
    }
}
