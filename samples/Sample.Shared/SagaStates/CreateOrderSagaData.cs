// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
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