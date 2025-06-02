using System;

namespace OrderService.Application.Features.Orders.Sagas.Events
{
    // This event is intended for publishing to an external message broker (e.g., RabbitMQ)
    public class OrderCreationFailedEvent
    {
        public Guid OrderId { get; set; } // Could be the intended OrderId or a correlation ID
        public string Reason { get; set; }
        public DateTime FailedAt { get; set; }

        // Parameterless constructor for deserialization
        public OrderCreationFailedEvent() {}

        public OrderCreationFailedEvent(Guid orderId, string reason, DateTime failedAt)
        {
            OrderId = orderId;
            Reason = reason;
            FailedAt = failedAt;
        }
    }
}
