// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Sample.Shared.Services;

public static class InventoryService
{
    public static void ReleaseStock(Guid messageOrderId)
    {
        Console.WriteLine($"Release Stock {messageOrderId}");
    }
}