using Microsoft.EntityFrameworkCore;

namespace Sample.Delivery.NetFramework481.Infrastructure.Persistence;

public sealed class DeliveryDbContext : DbContext
{
    public DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : base(options) { }

    public DbSet<Domain.Deliveries.Delivery> Deliveries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Domain.Deliveries.Delivery>(entity =>
        {
            entity.ToTable("Deliveries");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.OrderId).IsRequired();
            entity.Property(d => d.CustomerName).IsRequired().HasMaxLength(200);
            entity.Property(d => d.ShippingStreet).HasMaxLength(500);
            entity.Property(d => d.ShippingCity).HasMaxLength(100);
            entity.Property(d => d.ShippingState).HasMaxLength(100);
            entity.Property(d => d.ShippingZipCode).HasMaxLength(20);
            entity.Property(d => d.ShippingCountry).HasMaxLength(100);
            entity.Property(d => d.Status).IsRequired();
            entity.Property(d => d.TrackingNumber).IsRequired().HasMaxLength(50);
            entity.Property(d => d.DeliveryDate);

            entity.HasIndex(d => d.OrderId).IsUnique();
            entity.HasIndex(d => d.TrackingNumber).IsUnique();
        });
    }
}
