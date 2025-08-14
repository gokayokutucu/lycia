
namespace Sample_Net90.Choreography.Application.Interfaces.Repositories;

public interface ICustomerRepository
{
    Task<bool> AddressExistsAsync(Guid addressId);
    Task<bool> CardExistsAsync(Guid cartId);
    Task<bool> CustomerExistsAsync(Guid customerId);
}
