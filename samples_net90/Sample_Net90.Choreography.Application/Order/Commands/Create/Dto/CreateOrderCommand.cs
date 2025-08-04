using MediatR;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public sealed class CreateOrderCommand : IRequest<CreateOrderCommandResult>
{
    public Guid CustomerId { get; init; }
    public IEnumerable<CartItem> Cart { get; init; }
    public Guid DeliveryAddress { get; init; }
    public Guid BillingAddress { get; init; }
}
