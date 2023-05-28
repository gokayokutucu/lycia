using System.ComponentModel;

namespace Sample.Domain.Enums;

public enum OrderStatus
{
    [Description("Sipariş oluşturuldu.")]
    Created,
    
    [Description("Sipariş işleniyor.")]
    Processing,
    
    [Description("Sipariş gönderildi.")]
    Shipped,
    
    [Description("Sipariş teslim edildi.")]
    Delivered,
    
    [Description("Sipariş iptal edildi.")]
    Cancelled
}