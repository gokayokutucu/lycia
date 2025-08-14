namespace Sample.Shared.Services;

public static class InventoryService
{
    public static void ReleaseStock(Guid messageOrderId)
    {
        Console.WriteLine($"Release Stock {messageOrderId}");
    }
}