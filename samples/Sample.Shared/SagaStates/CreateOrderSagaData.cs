using Lycia.Messaging;

namespace Sample.Shared.SagaStates;

public class CreateOrderSagaData : SagaData
{
    public string OrderId { get; set; }
    public int RetryCount { get; set; }
    public bool InventoryCompensated { get; set; }
    public bool ShippingCompensated { get; set; }
    public bool PaymentIrreversible { get; set; }
    public bool ShippingReversed { get; set; }
}