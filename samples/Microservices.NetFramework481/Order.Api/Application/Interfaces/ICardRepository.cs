using System;
using System.Threading;
using System.Threading.Tasks;
using Sample.Order.NetFramework481.Domain.Customers;

namespace Sample.Order.NetFramework481.Application.Interfaces;

public interface ICardRepository
{
    Task<Card?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
