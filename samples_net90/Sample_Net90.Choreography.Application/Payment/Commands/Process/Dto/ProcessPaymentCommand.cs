using MediatR;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Process;

public sealed class ProcessPaymentCommand : IRequest<ProcessPaymentCommandResult>
{
    public Guid OrderId { get; init; }
    public Guid CardId { get; init; }
}
