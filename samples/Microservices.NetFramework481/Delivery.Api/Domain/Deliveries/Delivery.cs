using Sample.Delivery.NetFramework481.Domain.Common;
using System;

namespace Sample.Delivery.NetFramework481.Domain.Deliveries;

public sealed class Delivery : Entity
{
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; }
    public string ShippingStreet { get; set; }
    public string ShippingCity { get; set; }
    public string ShippingState { get; set; }
    public string ShippingZipCode { get; set; }
    public string ShippingCountry { get; set; }
    public DeliveryStatus Status { get; set; }
    public string TrackingNumber { get; set; }
    public DateTime? DeliveryDate { get; set; }
}
