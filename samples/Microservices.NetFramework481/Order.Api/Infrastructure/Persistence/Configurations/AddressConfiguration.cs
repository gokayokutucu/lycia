using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sample.Order.NetFramework481.Domain.Customers;

namespace Sample.Order.NetFramework481.Infrastructure.Persistence.Configurations;

/// <summary>
/// Entity Framework configuration for Address entity.
/// </summary>
public sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    /// <summary>
    /// Configures the Address entity mapping.
    /// </summary>
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Addresses");
        builder.HasKey(a => a.Id);

        // Base entity properties (CreatedAt, UpdatedAt)
        builder.ConfigureBaseEntity();

        builder.Property(a => a.CustomerId)
            .IsRequired();

        builder.Property(a => a.Street)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.State)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.ZipCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(a => a.Country)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.IsDefault)
            .IsRequired();

        // Relationship with Customer
        builder.HasOne<Customer>()
            .WithMany(c => c.Addresses)
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.CustomerId);
    }
}
