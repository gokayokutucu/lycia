// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;

namespace Lycia.Tests.SagaStates;

/// <summary>
/// Saga data for the order creation process.
/// Carries shared state across multiple steps of the order saga.
/// </summary>
public class SampleSagaData : SagaData
{
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public decimal TotalPrice { get; set; }
}