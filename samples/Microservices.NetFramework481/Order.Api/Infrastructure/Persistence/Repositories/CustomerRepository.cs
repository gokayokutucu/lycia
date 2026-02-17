using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sample.Order.NetFramework481.Application.Interfaces;
using Sample.Order.NetFramework481.Domain.Customers;
using Serilog;

namespace Sample.Order.NetFramework481.Infrastructure.Persistence.Repositories;

public sealed class CustomerRepository(OrderDbContext dbContext) : ICustomerRepository
{
    private static readonly ILogger Logger = Log.ForContext<CustomerRepository>();

    public async Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Logger.Information("📦 CustomerRepository.GetByIdAsync CALLED | Id: {Id}", id);
        try
        {
            var result = await dbContext.Customers
                .AsNoTracking()
                .Include(c => c.Addresses)
                .Include(c => c.Cards)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            Logger.Information("📦 CustomerRepository.GetByIdAsync COMPLETED | Found: {Found}", result != null);
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "📦 CustomerRepository.GetByIdAsync EXCEPTION | Id: {Id}", id);
            throw;
        }
    }

    public async Task SaveAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        await dbContext.Customers.AddAsync(customer, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        dbContext.Customers.Update(customer);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
