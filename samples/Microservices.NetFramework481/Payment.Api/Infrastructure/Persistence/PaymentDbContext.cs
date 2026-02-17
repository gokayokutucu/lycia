using Microsoft.EntityFrameworkCore;

namespace Sample.Payment.NetFramework481.Infrastructure.Persistence;

/// <summary>
/// Database context for Payment service.
/// </summary>
public sealed class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    public DbSet<Domain.Payments.Payment> Payments { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Domain.Payments.Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.TransactionId).IsRequired();
            entity.Property(p => p.OrderId).IsRequired();
            entity.Property(p => p.SaveCardId).IsRequired();
            entity.Property(p => p.Amount).HasColumnType("decimal(18,2)").IsRequired();
            entity.Property(p => p.Status).IsRequired();
            entity.Property(p => p.PaidAt);
            entity.Property(p => p.FailureReason).HasMaxLength(500).IsRequired(false);

            entity.HasIndex(p => p.OrderId);
            entity.HasIndex(p => p.TransactionId).IsUnique();
        });
    }
}
