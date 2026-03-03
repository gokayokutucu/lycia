using Microsoft.EntityFrameworkCore;

namespace Sample.Notification.NetFramework481.Infrastructure.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Notifications.Notification> Notifications { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Domain.Notifications.Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Recipient).IsRequired().HasMaxLength(500);
            entity.Property(n => n.Type).IsRequired();
            entity.Property(n => n.Subject).HasMaxLength(500);
            entity.Property(n => n.Message).IsRequired();
            entity.Property(n => n.Status).IsRequired();
            entity.Property(n => n.RelatedEntityType).HasMaxLength(50);
            entity.Property(n => n.SentAt);

            entity.HasIndex(n => n.Recipient);
            entity.HasIndex(n => n.RelatedEntityId);
        });
    }
}
