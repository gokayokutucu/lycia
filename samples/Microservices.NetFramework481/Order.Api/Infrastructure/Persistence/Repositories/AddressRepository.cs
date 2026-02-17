using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sample.Order.NetFramework481.Application.Interfaces;
using Sample.Order.NetFramework481.Domain.Customers;

namespace Sample.Order.NetFramework481.Infrastructure.Persistence.Repositories;

public sealed class AddressRepository(OrderDbContext dbContext) : IAddressRepository
{
    public async Task<Address?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Addresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task SaveAsync(Address address, CancellationToken cancellationToken = default)
    {
        await dbContext.Addresses.AddAsync(address, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Address address, CancellationToken cancellationToken = default)
    {
        dbContext.Addresses.Update(address);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
