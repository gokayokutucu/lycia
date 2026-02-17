using Microsoft.EntityFrameworkCore;

namespace Sample.Product.NetFramework481.Infrastructure.Persistence;

/// <summary>
/// Database context for Stock.Api.
/// </summary>
public sealed class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

    public DbSet<Domain.Products.Product> Products => Set<Domain.Products.Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Products.Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Price).HasColumnType("decimal(18,2)").IsRequired();
            entity.Property(p => p.StockQuantity).IsRequired();
            entity.Property(p => p.ReservedQuantity).IsRequired();
            entity.Ignore(p => p.AvailableQuantity); // Computed property
        });
    }
}

