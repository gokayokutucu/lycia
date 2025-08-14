using Sample_Net90.Choreography.Application.Interfaces.Services;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Infrastructure.Services;

public sealed class DeliveryService : IDeliveryService
{
    public async Task CancelShipmentAsync(Shipment shipment)
    {
        
    }

    public async Task<Guid> ScheduleShipmentAsync(Shipment shipment)
    {
        return Guid.CreateVersion7();
    }
}
