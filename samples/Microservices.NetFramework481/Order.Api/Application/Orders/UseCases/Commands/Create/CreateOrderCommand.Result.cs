using System;
using Shared.Contracts.Enums;

namespace Sample.Order.NetFramework481.Application.Orders.UseCases.Commands.Create;

/// <summary>
/// Result of CreateOrderCommand.
/// </summary>
public sealed class CreateOrderCommandResult
{
    /// <summary>
    /// Created order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Order status.
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// Total amount.
    /// </summary>
    public decimal TotalAmount { get; set; }
}
