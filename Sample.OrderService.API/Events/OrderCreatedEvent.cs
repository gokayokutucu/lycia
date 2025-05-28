using System;
using Lycia.Messaging; // For EventBase

namespace Sample.OrderService.API.Events
{
    public class OrderCreatedEvent : EventBase // Inherits MessageId, Timestamp, ApplicationId, and now SagaId
    {
        // The SagaId property is now inherited from EventBase.

        public Guid OrderId { get; set; }

        public OrderDetailsDto OrderDetails { get; set; }

        public OrderCreatedEvent(Guid sagaId, Guid orderId, OrderDetailsDto orderDetails)
        {
            this.SagaId = sagaId; // Set the inherited SagaId property
            OrderId = orderId;
            OrderDetails = orderDetails;
        }
    }
}
