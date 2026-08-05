using Sample.Delivery.NetFramework481.Domain.Common;
using System;

namespace Sample.Delivery.NetFramework481.Domain.Deliveries;

public sealed class Delivery : Entity
{
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string ShippingStreet { get; set; } = string.Empty;
    public string ShippingCity { get; set; } = string.Empty;
    public string ShippingState { get; set; } = string.Empty;
    public string ShippingZipCode { get; set; } = string.Empty;
    public string ShippingCountry { get; set; } = string.Empty;
    public DeliveryStatus Status { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public DateTime? DeliveryDate { get; set; }
}
