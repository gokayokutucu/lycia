using MediatR;
using Shared.Contracts.Dtos;
using System;
using System.Collections.Generic;

namespace Sample.Order.NetFramework481.Application.Orders.UseCases.Commands.Create;

public sealed class CreateOrderCommand : IRequest<Unit>
{
    public Guid CustomerId { get; private set; } = Guid.Empty;
    public Guid AddressId { get; private set; } = Guid.Empty;
    public Guid CardId { get; private set; } = Guid.Empty;

    public List<OrderItemDto> Items { get; private set; } = [];

    public static CreateOrderCommand Create(
        Guid customerId,
        Guid addressId,
        Guid cardId,
        List<OrderItemDto> items) 
    => new()
    {
        CustomerId = customerId,
        AddressId = addressId,
        CardId = cardId,
        Items = items,
    };
}
