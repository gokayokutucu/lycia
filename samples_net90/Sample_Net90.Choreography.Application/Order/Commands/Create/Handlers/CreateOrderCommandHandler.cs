using Lycia.Saga.Abstractions;
using MapsterMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Domain.Sagas.Order.CreateOrder.Commands;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public class CreateOrderCommandHandler(IEventBus eventBus, IMapper mapper, ILogger<CreateOrderCommandHandler> logger)
    : IRequestHandler<CreateOrderCommand, CreateOrderCommandResult>
{
    public async Task<CreateOrderCommandResult> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var command = mapper.Map<CreateOrderSagaCommand>(request);
        command.OrderId = Guid.CreateVersion7();
        await eventBus.Send(command);

        return CreateOrderCommandResult.Create(command.OrderId);
    }
}
