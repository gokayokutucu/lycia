using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sample.Order.NetFramework481.Domain.Common;

namespace Sample.Order.NetFramework481.Infrastructure.Persistence.Configurations;

/// <summary>
/// Base configuration for all entities that inherit from Entity base class.
/// Configures common properties like CreatedAt and UpdatedAt.
/// </summary>
public static class EntityConfigurationExtensions
{
    /// <summary>
    /// Configures base entity properties (CreatedAt, UpdatedAt) for any entity.
    /// </summary>
    public static void ConfigureBaseEntity<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : Entity
    {
        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(e => e.UpdatedAt)
            .IsRequired(false);
    }
}
