// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;

namespace Sample.Shared.Messages.Commands;

/// <summary>
/// Command to initiate an order creation saga.
/// </summary>
public class CreateOrderCommand : CommandBase
{
    public Guid OrderId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public decimal TotalPrice { get; set; }
}