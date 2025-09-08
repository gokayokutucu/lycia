// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
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