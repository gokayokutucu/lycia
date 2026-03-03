using Sample.Product.NetFramework481.Domain.Common;

namespace Sample.Product.NetFramework481.Domain.Products;

public sealed class Product : Entity
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int AvailableQuantity => StockQuantity - ReservedQuantity;
}
