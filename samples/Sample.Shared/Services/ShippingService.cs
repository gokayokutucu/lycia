namespace Sample.Shared.Services;

public static class ShippingService
{
    public static bool TryShip(Guid evtOrderId, bool isShipped = true)
    {
        // Check if the order ID is valid
        if (evtOrderId == Guid.Empty)
        {
            throw new ArgumentException("Invalid order ID.", nameof(evtOrderId));
        }

        // Simulate shipping logic
        return isShipped;
    }
}