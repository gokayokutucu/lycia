using Sample.Payment.NetFramework481.Domain.Common;
using Shared.Contracts.Enums;
using System;

namespace Sample.Payment.NetFramework481.Domain.Payments;

public sealed class Payment : Entity
{
    public Guid TransactionId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public Guid SaveCardId { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime? PaidAt { get; set; }
    public string FailureReason { get; set; }
}
