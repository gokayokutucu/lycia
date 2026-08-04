// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging;
using Sample.Shared.Abstractions.Endpoints;

namespace Sample.Shared.Messages.Commands;

/// <summary>Demonstrates a command whose owner is derived as <c>StockService</c>.</summary>
public sealed class ReserveStockCommand : CommandBase, IStockServiceCommand
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}
