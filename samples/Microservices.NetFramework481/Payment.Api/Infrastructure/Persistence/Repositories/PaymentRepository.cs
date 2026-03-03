using Microsoft.EntityFrameworkCore;
using Sample.Payment.NetFramework481.Application.Interfaces;
using Sample.Payment.NetFramework481.Domain.Payments;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Payment.NetFramework481.Infrastructure.Persistence.Repositories;

public sealed class PaymentRepository(PaymentDbContext context) : IPaymentRepository
{
    public async Task<Domain.Payments.Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => await context.Payments.FindAsync([paymentId], cancellationToken);

    public async Task<Domain.Payments.Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await context.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    public async Task SaveAsync(Domain.Payments.Payment payment, CancellationToken cancellationToken = default)
    {
        await context.Payments.AddAsync(payment, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(Guid paymentId, PaymentStatus status, CancellationToken cancellationToken = default)
    {
        var payment = await context.Payments.FindAsync([paymentId], cancellationToken);
        if (payment == null)
            throw new InvalidOperationException($"Payment not found: {paymentId}");

        context.Entry(payment).Property(nameof(Domain.Payments.Payment.Status)).CurrentValue = status;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ProcessPaymentAsync(
        Guid paymentId,
        string cardHolderName,
        string cardLast4Digits,
        int cardExpiryMonth,
        int cardExpiryYear,
        CancellationToken cancellationToken = default)
    {
        var payment = await GetByIdAsync(paymentId, cancellationToken);
        if (payment == null)
            throw new InvalidOperationException($"Payment not found: {paymentId}");

        context.Entry(payment).Property(nameof(Domain.Payments.Payment.Status)).CurrentValue = PaymentStatus.Processing;
        await context.SaveChangesAsync(cancellationToken);

        // Simulate payment gateway call with card details
        // In real implementation: Call bank/payment gateway API with card info
        // Example: var response = await paymentGateway.ChargeAsync(amount, cardHolderName, cardLast4, expiry, cvv);

        await Task.Delay(100, cancellationToken);

        // Log payment attempt (PCI DSS compliant - no sensitive data in logs)
        var maskedCard = $"****-****-****-{cardLast4Digits}";
        // Logger would log: "Processing payment with card: {maskedCard}, Holder: {cardHolderName}, Expiry: {cardExpiryMonth}/{cardExpiryYear}"

        var success = new Random().Next(0, 10) > 1; // 90% success rate

        if (success)
        {
            context.Entry(payment).Property(nameof(Domain.Payments.Payment.Status)).CurrentValue = PaymentStatus.Completed;
            context.Entry(payment).Property(nameof(Domain.Payments.Payment.PaidAt)).CurrentValue = DateTime.UtcNow;
        }
        else
        {
            context.Entry(payment).Property(nameof(Domain.Payments.Payment.Status)).CurrentValue = PaymentStatus.Failed;
            context.Entry(payment).Property(nameof(Domain.Payments.Payment.FailureReason)).CurrentValue = "Payment gateway declined the transaction";
        }

        await context.SaveChangesAsync(cancellationToken);
        return success;
    }
}
