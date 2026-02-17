using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sample.Order.NetFramework481.Application.Interfaces;
using Sample.Order.NetFramework481.Domain.Customers;

namespace Sample.Order.NetFramework481.Infrastructure.Persistence.Repositories;

public sealed class CardRepository(OrderDbContext dbContext) : ICardRepository
{
    public async Task<Card?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Cards
            .AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == id, cancellationToken);
}
