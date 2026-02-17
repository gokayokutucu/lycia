using System;
using MediatR;
using Sample.Payment.NetFramework481.Domain.Payments;

namespace Sample.Payment.NetFramework481.Application.Payments.UseCases.Commands.Process;

/// <summary>
/// Command to process a payment.
/// </summary>
public sealed class ProcessPaymentCommand : IRequest<ProcessPaymentCommandResult>
{
    /// <summary>
    /// Order ID.
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// Creates a new instance of ProcessPaymentCommand.
    /// </summary>
    public static ProcessPaymentCommand Create(Guid orderId)
        => new()
        {
            OrderId = orderId
        };
}
