using System;
using System.Threading;
using System.Threading.Tasks;
using Sample.Order.NetFramework481.Domain.Customers;

namespace Sample.Order.NetFramework481.Application.Interfaces;

public interface IAddressRepository
{
    Task<Address?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(Address address, CancellationToken cancellationToken = default);
    Task UpdateAsync(Address address, CancellationToken cancellationToken = default);
}
