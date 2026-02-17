using System;
using System.Threading;
using System.Threading.Tasks;
using Sample.Order.NetFramework481.Domain.Customers;

namespace Sample.Order.NetFramework481.Application.Interfaces;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(Customer customer, CancellationToken cancellationToken = default);
    Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default);
}
