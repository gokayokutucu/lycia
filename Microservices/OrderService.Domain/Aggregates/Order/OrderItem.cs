using System;

namespace OrderService.Domain.Aggregates.Order
{
    public class OrderItem
    {
        public Guid ProductId { get; private set; }
        public string ProductName { get; private set; } // Optional, denormalized
        public decimal UnitPrice { get; private set; }
        public int Quantity { get; private set; }

        // Private constructor for ORM or deserialization
        private OrderItem() {}

        public OrderItem(Guid productId, string productName, decimal unitPrice, int quantity)
        {
            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
            }
            if (unitPrice < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
            }

            ProductId = productId;
            ProductName = productName; // Ensure this is handled if it's truly optional or comes from elsewhere
            UnitPrice = unitPrice;
            Quantity = quantity;
        }

        // Public method to allow quantity changes, if business rules permit
        public void UpdateQuantity(int newQuantity)
        {
            if (newQuantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(newQuantity), "New quantity must be greater than zero.");
            }
            Quantity = newQuantity;
            // Note: If quantity changes, the Order's TotalPrice would need recalculation.
            // This can be handled by the Order aggregate or by raising an event.
        }
    }
}
