using Sample_Net90.Choreography.Application.Interfaces.Repositories;

namespace Sample_Net90.Choreography.Infrastructure.Repositories;

public sealed class CustomerRepository : ICustomerRepository
{
    public async Task<bool> AddressExistsAsync(Guid addressId)
    {
        return true;
    }

    public async Task<bool> CardExistsAsync(Guid cartId)
    {
        return true;
    }

    public async Task<bool> CustomerExistsAsync(Guid customerId)
    {
        return true;
    }
}
