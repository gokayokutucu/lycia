using Shared.Contracts.Dtos;
using Shared.Contracts.Enums;
using System;
using System.Collections.Generic;

namespace Sample.Order.NetFramework481.Application.Orders.UseCases.Queries.Get;

/// <summary>
/// Result of GetOrderQuery.
/// </summary>
public sealed class GetOrderQueryResult
{
    /// <summary>
    /// Order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Customer name.
    /// </summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>
    /// Customer email.
    /// </summary>
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>
    /// Order status.
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// Total amount.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Order items.
    /// </summary>
    public List<OrderItemDto> Items { get; set; } = [];

    /// <summary>
    /// Created timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last updated timestamp.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
