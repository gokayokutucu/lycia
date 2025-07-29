using MediatR;
using System.Net;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public class CreateOrderCommand : IRequest<CreateOrderCommandResult>
{
}
