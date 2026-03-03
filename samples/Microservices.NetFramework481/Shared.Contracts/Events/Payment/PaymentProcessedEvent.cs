using Lycia.Saga.Messaging;
using System;

namespace Shared.Contracts.Events.Payment;

public sealed class PaymentProcessedEvent : EventBase
{
    public PaymentProcessedEvent()
    {
        
    }
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;

    public string ShippingStreet { get; set; } = string.Empty;
    public string ShippingCity { get; set; } = string.Empty;
    public string ShippingState { get; set; } = string.Empty;
    public string ShippingZipCode { get; set; } = string.Empty;
    public string ShippingCountry { get; set; } = string.Empty;
}
