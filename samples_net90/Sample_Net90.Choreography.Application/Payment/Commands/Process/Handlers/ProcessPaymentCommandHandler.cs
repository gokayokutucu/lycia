using Lycia.Abstractions;
using MapsterMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Order.Commands.Create;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Commands;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Process;

public sealed class ProcessPaymentCommandHandler(IEventBus eventBus, IMapper mapper, ILogger<CreateOrderCommandHandler> logger)
    : IRequestHandler<ProcessPaymentCommand, ProcessPaymentCommandResult>
{
    public async Task<ProcessPaymentCommandResult> Handle(ProcessPaymentCommand request, CancellationToken cancellationToken)
    {
        var command = mapper.Map<ProcessPaymentSagaCommand>(request);

        await eventBus.Send(command);

        return mapper.Map<ProcessPaymentCommandResult>(null);
    }
}
