using Lycia.Messaging;


namespace Sample_Net21.Shared.Messages.Events
{
    public sealed class PaymentRefundedEvent : EventBase
    {
        public decimal Amount { get; set; }
        public string Reason { get; set; } = "Compensating";
        public static PaymentRefundedEvent Create(string reason, decimal amount)
            => new PaymentRefundedEvent
            {
                Reason = reason,
                Amount = amount
            };
    }
}
