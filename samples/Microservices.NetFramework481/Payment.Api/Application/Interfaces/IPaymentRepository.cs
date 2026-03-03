using System;
using System.Threading;
using System.Threading.Tasks;
using Sample.Payment.NetFramework481.Domain.Payments;

namespace Sample.Payment.NetFramework481.Application.Interfaces;

/// <summary>
/// Repository interface for Payment aggregate.
/// </summary>
public interface IPaymentRepository
{
    Task<Domain.Payments.Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<Domain.Payments.Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SaveAsync(Domain.Payments.Payment payment, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid paymentId, PaymentStatus status, CancellationToken cancellationToken = default);
    Task<bool> ProcessPaymentAsync(
        Guid paymentId, 
        string cardHolderName,
        string cardLast4Digits,
        int cardExpiryMonth,
        int cardExpiryYear,
        CancellationToken cancellationToken = default);
}
