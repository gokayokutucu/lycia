using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sample.Order.NetFramework481.Infrastructure.Persistence.Configurations;

/// <summary>
/// Entity Framework configuration for Order entity.
/// </summary>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Domain.Orders.Order>
{
    /// <summary>
    /// Configures the Order entity mapping.
    /// </summary>
    public void Configure(EntityTypeBuilder<Domain.Orders.Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        // Base entity properties (CreatedAt, UpdatedAt)
        builder.ConfigureBaseEntity();

        builder.Property(o => o.CustomerId)
            .IsRequired();

        builder.Property(o => o.ShippingAddressId)
            .IsRequired();

        builder.Property(o => o.SavedCardId)
            .IsRequired();

        builder.Property(o => o.Status)
            .IsRequired();

        builder.Property(o => o.TotalAmount)
            .HasColumnType("decimal(18,2)");

        // Relationships with Customer (same DB)
        builder.HasOne<Domain.Customers.Customer>()
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relationships with Address (same DB)
        builder.HasOne<Domain.Customers.Address>()
            .WithMany()
            .HasForeignKey(o => o.ShippingAddressId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relationships with SavedCard (same DB)
        builder.HasOne<Domain.Customers.Card>()
            .WithMany()
            .HasForeignKey(o => o.SavedCardId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relationship with OrderItems
        builder.HasMany<Domain.Orders.OrderItem>(o => o.Items)
            .WithOne()
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
