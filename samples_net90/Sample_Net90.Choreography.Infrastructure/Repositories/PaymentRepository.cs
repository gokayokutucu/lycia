using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Infrastructure.Repositories;

public sealed class PaymentRepository : IPaymentRepository
{
    public async Task<Guid> ProcessAsync(Payment payment)
    {
        return Guid.CreateVersion7(); 
    }

    public async Task RefundAsync(Payment payment)
    {

    }
}
