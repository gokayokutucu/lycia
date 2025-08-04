

namespace Sample_Net90.Choreography.Application.Interfaces.Services;

public interface IDeliveryService
{
    Task CancelShipmentAsync(Domain.Entities.Shipment shipment);
    Task<Guid> ScheduleShipmentAsync(Domain.Entities.Shipment shipment);
}
