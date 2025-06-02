using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrderService.Application.Contracts.Persistence;
using OrderService.Domain.Aggregates.Order;

namespace OrderService.Infrastructure.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        // In-memory store for demonstration purposes
        private static readonly List<Order> _orders = new List<Order>();

        public Task AddAsync(Order order, CancellationToken cancellationToken = default)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            // Simulate CancellationToken usage if needed for async operations in a real DB
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            // Check for duplicates, though Guids make this less likely for Id.
            // More relevant if there were unique constraints on other properties.
            if (_orders.Any(o => o.Id == order.Id))
            {
                // In a real DB, this might throw a specific exception from the DB driver
                // For in-memory, we can choose to throw or handle as an update (though AddAsync implies new)
                throw new InvalidOperationException($"An order with ID {order.Id} already exists.");
            }

            _orders.Add(order);
            return Task.CompletedTask;
        }

        public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            // Simulate CancellationToken usage
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<Order?>(cancellationToken);
            }

            var order = _orders.FirstOrDefault(o => o.Id == id);
            return Task.FromResult(order);
        }

        // public Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
        // {
        //     if (order == null)
        //     {
        //         throw new ArgumentNullException(nameof(order));
        //     }
        //     var existingOrderIndex = _orders.FindIndex(o => o.Id == order.Id);
        //     if (existingOrderIndex != -1)
        //     {
        //         _orders[existingOrderIndex] = order;
        //     }
        //     else
        //     {
        //         throw new InvalidOperationException($"Order with ID {order.Id} not found for update.");
        //     }
        //     return Task.CompletedTask;
        // }
    }
}
