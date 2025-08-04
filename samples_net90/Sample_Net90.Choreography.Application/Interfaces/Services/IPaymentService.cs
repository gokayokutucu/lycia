

namespace Sample_Net90.Choreography.Application.Interfaces.Services;

public interface IPaymentService
{
    Task<Guid> ProcessPaymentAsync(Domain.Entities.Payment payment);
    Task RefundPaymentAsync(Domain.Entities.Payment payment);
}
