using System;
using System.Threading;
using System.Threading.Tasks;
using Mapster;
using MediatR;
using Microsoft.Extensions.Logging;
using Sample.Payment.NetFramework481.Application.Interfaces;
using Sample.Payment.NetFramework481.Domain.Payments;
using PaymentEntity = Sample.Payment.NetFramework481.Domain.Payments.Payment;

namespace Sample.Payment.NetFramework481.Application.Payments.UseCases.Commands.Process;

/// <summary>
/// Handler for ProcessPaymentCommand.
/// This is a UseCase handler that returns payment status for API response.
/// Actual payment processing happens in ProcessPaymentSagaHandler.
/// </summary>
public sealed class ProcessPaymentCommandHandler(
    IPaymentRepository paymentRepository,
    ILogger<ProcessPaymentCommandHandler> logger) : IRequestHandler<ProcessPaymentCommand, ProcessPaymentCommandResult>
{
    public async Task<ProcessPaymentCommandResult> Handle(ProcessPaymentCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting payment status for order: {OrderId}", request.OrderId);

        // Get existing payment created by saga
        var payment = await paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);

        if (payment == null)
        {
            logger.LogWarning("Payment not found for order: {OrderId}", request.OrderId);
            throw new InvalidOperationException($"Payment not found for order {request.OrderId}");
        }

        logger.LogInformation("Payment found: {PaymentId}, Status: {Status}", payment.Id, payment.Status);

        return payment.Adapt<ProcessPaymentCommandResult>();
    }
}
