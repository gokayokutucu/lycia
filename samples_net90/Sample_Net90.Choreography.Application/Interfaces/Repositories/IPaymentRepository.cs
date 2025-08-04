
namespace Sample_Net90.Choreography.Application.Interfaces.Repositories;

public interface IPaymentRepository
{
    Task<Guid> ProcessAsync(Domain.Entities.Payment payment);
    Task RefundAsync(Domain.Entities.Payment payment);
}
