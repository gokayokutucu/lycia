using Lycia.Saga.Abstractions;
using Mapster;
using MediatR;
using Shared.Contracts.Commands;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Order.NetFramework481.Application.Orders.UseCases.Commands.Create;

public sealed class CreateOrderCommandHandler(IEventBus eventBus) : IRequestHandler<CreateOrderCommand, Unit>
{
    public async Task<Unit> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var sagaCommand = request.Adapt<CreateOrderSagaCommand>();
        await eventBus.Send(sagaCommand);
        return Unit.Value;
    }
}
