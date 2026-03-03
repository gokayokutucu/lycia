using System;
using Lycia.Saga.Messaging;

namespace Shared.Contracts.Events.Stock;

public sealed class StockReservedEvent : EventBase
{
    public StockReservedEvent()
    {
        
    }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ShippingAddressId { get; set; }
    public Guid SavedCardId { get; set; }
    public decimal TotalAmount { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;

    public string ShippingStreet { get; set; } = string.Empty;
    public string ShippingCity { get; set; } = string.Empty;
    public string ShippingState { get; set; } = string.Empty;
    public string ShippingZipCode { get; set; } = string.Empty;
    public string ShippingCountry { get; set; } = string.Empty;

    public string CardHolderName { get; set; } = string.Empty;
    public string CardLast4Digits { get; set; } = string.Empty;
    public int CardExpiryMonth { get; set; }
    public int CardExpiryYear { get; set; }
}
