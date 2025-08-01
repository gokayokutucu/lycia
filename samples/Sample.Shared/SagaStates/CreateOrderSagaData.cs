using Lycia.Saga;

namespace Sample.Shared.SagaStates;

public class CreateOrderSagaData
{
    public string OrderId { get; set; }
    public int RetryCount { get; set; }
    public bool IsCompleted { get; set; }
    public bool InventoryCompensated { get; set; }
    public bool ShippingCompensated { get; set; }
    public bool PaymentIrreversible { get; set; }
}