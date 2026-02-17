using Lycia.Saga.Messaging;
using Shared.Contracts.Dtos;
using System;
using System.Collections.Generic;

namespace Shared.Contracts.Commands;

public sealed class CreateOrderSagaCommand : CommandBase
{
    public CreateOrderSagaCommand()
    {
        
    }

    public Guid CustomerId { get; set; }

    public List<OrderItemDto> Items { get; set; } = [];

    public Guid ShippingAddressId { get; set; }

    public Guid SavedCardId { get; set; }
}
