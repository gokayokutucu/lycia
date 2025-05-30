using System;
using System.Collections.Generic;
using Lycia.Saga;
using Sample.Shared.Messages.Commands; // For OrderItem

namespace Sample.Shared.Messages.Sagas
{
    public class LyciaSagaData : SagaData
    {
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public decimal TotalPrice { get; set; }
        public List<OrderItem> Items { get; set; }
        public string CardDetails { get; set; } // Placeholder
        public string ShippingAddress { get; set; }
        public string UserEmail { get; set; }
        public string OrderStatus { get; set; } // e.g., "Pending", "InventoryUpdated", "PaymentProcessed", "Shipped", "Completed", "Failed"
        public Guid PaymentId { get; set; }
        public string ShipmentTrackingNumber { get; set; }
        public string FailureReason { get; set; }
    }
}
