using Sample_Net90.Choreography.Application.Interfaces.Services;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Infrastructure.Services;

public sealed class PaymentService : IPaymentService
{
    public async Task<Guid> ProcessPaymentAsync(Payment payment)
    {
        return Guid.CreateVersion7();
    }

    public async Task RefundPaymentAsync(Payment payment)
    {
        
    }
}
