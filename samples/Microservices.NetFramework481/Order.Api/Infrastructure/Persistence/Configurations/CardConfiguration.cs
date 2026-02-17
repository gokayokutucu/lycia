using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sample.Order.NetFramework481.Domain.Customers;

namespace Sample.Order.NetFramework481.Infrastructure.Persistence.Configurations;

/// <summary>
/// Entity Framework configuration for SavedCard entity.
/// </summary>
public sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    /// <summary>
    /// Configures the SavedCard entity mapping.
    /// </summary>
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.ToTable("Cards");  // Fixed: was "SavedCards" but DB table is "Cards"
        builder.HasKey(sc => sc.Id);

        // Base entity properties (CreatedAt, UpdatedAt)
        builder.ConfigureBaseEntity();

        builder.Property(sc => sc.CustomerId)
            .IsRequired();

        builder.Property(sc => sc.Last4Digits)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(sc => sc.ExpiryMonth)
            .IsRequired();

        builder.Property(sc => sc.ExpiryYear)
            .IsRequired();

        builder.Property(sc => sc.CardHolderName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(sc => sc.IsDefault)
            .IsRequired();

        // Relationship with Customer
        builder.HasOne<Customer>()
            .WithMany(c => c.Cards)
            .HasForeignKey(sc => sc.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(sc => sc.CustomerId);
    }
}
