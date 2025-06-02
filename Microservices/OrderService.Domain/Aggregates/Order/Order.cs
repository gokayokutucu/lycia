using System;
using System.Collections.Generic;
using System.Linq;

namespace OrderService.Domain.Aggregates.Order
{
    public class Order // Aggregate Root
    {
        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        private readonly List<OrderItem> _items = new List<OrderItem>();
        public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
        public decimal TotalPrice { get; private set; }
        public OrderStatus Status { get; private set; }
        public DateTime CreatedDate { get; private set; }
        public DateTime? UpdatedDate { get; private set; }

        // Private constructor for ORM/Deserialization
        private Order() { }

        public Order(Guid id, Guid userId, List<OrderItem> items)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("Order ID cannot be empty.", nameof(id));
            }
            if (userId == Guid.Empty)
            {
                throw new ArgumentException("User ID cannot be empty.", nameof(userId));
            }
            if (items == null || !items.Any())
            {
                throw new ArgumentException("Order must have at least one item.", nameof(items));
            }

            Id = id;
            UserId = userId;
            _items.AddRange(items); // Add initial items

            RecalculateTotalPrice(); // Calculate initial total price
            Status = OrderStatus.Pending;
            CreatedDate = DateTime.UtcNow;
            UpdatedDate = null;

            // TODO: Add domain event: new OrderCreatedDomainEvent(Id, UserId, CreatedDate)
        }

        public void AddOrderItem(OrderItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            // Check if item with same ProductId already exists; if so, consider updating quantity or disallow
            var existingItem = _items.FirstOrDefault(i => i.ProductId == item.ProductId);
            if (existingItem != null)
            {
                // Example: update quantity and recalculate price
                // This specific logic depends on business requirements (e.g. merge, throw, replace)
                existingItem.UpdateQuantity(existingItem.Quantity + item.Quantity);
            }
            else
            {
                _items.Add(item);
            }

            RecalculateTotalPrice();
            SetUpdatedDate();
            // TODO: Add domain event if significant
        }

        public void SetStatus(OrderStatus newStatus)
        {
            if (Status == newStatus) return;

            // Add any business logic for status transitions here
            // For example, cannot go from Shipped back to Pending, etc.
            // if (Status == OrderStatus.Completed || Status == OrderStatus.Cancelled)
            // {
            //     throw new InvalidOperationException($"Cannot change status from {Status} to {newStatus}.");
            // }

            Status = newStatus;
            SetUpdatedDate();
            // TODO: Add domain event: new OrderStatusChangedDomainEvent(...)
        }

        private void RecalculateTotalPrice()
        {
            TotalPrice = _items.Sum(item => item.UnitPrice * item.Quantity);
        }

        private void SetUpdatedDate()
        {
            UpdatedDate = DateTime.UtcNow;
        }
    }
}
