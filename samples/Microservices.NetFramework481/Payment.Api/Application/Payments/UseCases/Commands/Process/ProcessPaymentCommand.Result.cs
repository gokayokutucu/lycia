using System;
using Sample.Payment.NetFramework481.Domain.Payments;

namespace Sample.Payment.NetFramework481.Application.Payments.UseCases.Commands.Process;

/// <summary>
/// Result of ProcessPaymentCommand.
/// </summary>
public sealed class ProcessPaymentCommandResult
{
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public PaymentStatus Status { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
    public string FailureReason { get; set; } = string.Empty;
}
