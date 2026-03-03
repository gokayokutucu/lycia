using Microsoft.EntityFrameworkCore;

namespace Sample.Order.NetFramework481.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core DbContext for Order microservice.
/// </summary>
/// <remarks>
/// Initializes a new instance of the OrderDbContext.
/// </remarks>
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{

    /// <summary>
    /// Orders collection.
    /// </summary>
    public DbSet<Domain.Orders.Order> Orders { get; set; } = null!;

    /// <summary>
    /// Order items collection.
    /// </summary>
    public DbSet<Domain.Orders.OrderItem> OrderItems { get; set; } = null!;

    /// <summary>
    /// Customers collection.
    /// </summary>
    public DbSet<Domain.Customers.Customer> Customers { get; set; } = null!;

    /// <summary>
    /// Addresses collection.
    /// </summary>
    public DbSet<Domain.Customers.Address> Addresses { get; set; } = null!;

    /// <summary>
    /// Cards collection.
    /// </summary>
    public DbSet<Domain.Customers.Card> Cards { get; set; } = null!;

    /// <summary>
    /// Configures entity mappings.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations
        modelBuilder.ApplyConfiguration(new Configurations.OrderConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.OrderItemConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.AddressConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.CardConfiguration());
    }
}
