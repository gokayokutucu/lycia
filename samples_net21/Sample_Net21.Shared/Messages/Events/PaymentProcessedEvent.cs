using Lycia.Messaging;
using System;


namespace Sample_Net21.Shared.Messages.Events
{
    public sealed class PaymentProcessedEvent : EventBase
    {
        public Guid CustomerId { get; private set; }
        public decimal Amount { get; set; }
        public Guid OrderId { get; private set; }

        public static PaymentProcessedEvent Create(Guid orderId, Guid customerId, decimal amount)
            => new PaymentProcessedEvent()
            {
                OrderId = orderId,
                CustomerId = customerId,
                Amount = amount
            };
    }
}