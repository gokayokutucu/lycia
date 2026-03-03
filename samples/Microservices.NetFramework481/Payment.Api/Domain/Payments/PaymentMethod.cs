namespace Sample.Payment.NetFramework481.Domain.Payments;

/// <summary>
/// Payment method enumeration.
/// </summary>
public enum PaymentMethod
{
    /// <summary>
    /// Credit card payment.
    /// </summary>
    CreditCard = 0,

    /// <summary>
    /// PayPal payment.
    /// </summary>
    PayPal = 1,

    /// <summary>
    /// Bank transfer.
    /// </summary>
    BankTransfer = 2,

    /// <summary>
    /// Cash on delivery.
    /// </summary>
    CashOnDelivery = 3
}
