using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;
using DomainPaymentStatus = Sample.Payment.NetFramework481.Domain.Payments.PaymentStatus;

namespace Sample.Payment.NetFramework481.Infrastructure.Persistence;

public static class DbInitializer
{
    private static readonly ILogger Logger = Log.ForContext(typeof(DbInitializer));

    public static async Task InitializeAsync(PaymentDbContext context)
    {
        try
        {
            Logger.Information("🔧 [Payment] Initializing database...");

            // Wait for SQL Server to be ready (retry logic)
            var maxRetries = 10;
            var retryDelay = TimeSpan.FromSeconds(3);

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (await context.Database.CanConnectAsync())
                    {
                        Logger.Information("✅ [Payment] Successfully connected to database");
                        break;
                    }
                }
                catch (Exception)
                {
                    if (i == maxRetries - 1)
                    {
                        Logger.Error("❌ [Payment] Failed to connect to database after {Retries} retries", maxRetries);
                        throw;
                    }

                    Logger.Warning("⚠️ [Payment] SQL Server not ready, waiting {Delay}s... (Attempt {Current}/{Max})", 
                        retryDelay.TotalSeconds, i + 1, maxRetries);
                    await Task.Delay(retryDelay);
                }
            }

            // Check pending migrations
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            
            Logger.Information($"📊 [Payment] Pending migrations: {pendingMigrations.Count}");
            Logger.Information($"📊 [Payment] Applied migrations: {appliedMigrations.Count}");
            
            if (pendingMigrations.Any())
            {
                Logger.Information($"⚙️ [Payment] Applying {pendingMigrations.Count} pending migrations...");
                foreach (var migration in pendingMigrations)
                {
                    Logger.Information($"  - {migration}");
                }
                await context.Database.MigrateAsync();
                Logger.Information("✅ [Payment] Database migrations applied");
            }
            else if (appliedMigrations.Any())
            {
                Logger.Information("✅ [Payment] Database is up to date");
            }
            else
            {
                Logger.Warning("⚠️ [Payment] No migrations found! Ensure migrations assembly is configured correctly.");
                Logger.Information("⚙️ [Payment] Creating database using EnsureCreated as fallback...");
                await context.Database.EnsureCreatedAsync();
            }

            await SeedDataAsync(context);
            Logger.Information("✅ [Payment] Database initialization completed");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ [Payment] Database initialization failed");
            throw;
        }
    }

    private static async Task SeedDataAsync(PaymentDbContext context)
    {
        try
        {
            // Check if tables exist and are accessible
            var canConnect = await context.Database.CanConnectAsync();
            if (!canConnect)
            {
                Logger.Error("❌ [Payment] Cannot connect to database");
                throw new InvalidOperationException("Cannot connect to database");
            }

            Logger.Information("✅ [Payment] Database connection established");

            // Check if data already exists
            if (await context.Payments.AnyAsync())
            {
                Logger.Information("⏭️ [Payment] Database already seeded, skipping seed data");
                return;
            }

            Logger.Information("🌱 [Payment] Seeding initial data...");

            // Create sample payments using EF Core entities
            var payment1 = new Domain.Payments.Payment
            {
                TransactionId = Guid.NewGuid(),
                OrderId = Guid.Parse("62c8cbf8-d0fd-4bac-b2d8-03c1ce2460ae"),
                SaveCardId = Guid.Parse("54616a07-ce09-40e2-b8e7-5d44b0031350"),
                Amount = 199.99m,
                Status = DomainPaymentStatus.Completed,
                PaidAt = DateTime.UtcNow,
                FailureReason = string.Empty // Add this line
            };
            context.Payments.Add(payment1);
            context.Entry(payment1).Property("Id").CurrentValue = Guid.Parse("b1111111-1111-1111-1111-111111111111");

            var payment2 = new Domain.Payments.Payment
            {
                TransactionId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                SaveCardId = Guid.NewGuid(),
                Amount = 299.99m,
                Status = DomainPaymentStatus.Pending,
                FailureReason = string.Empty // Add this line
            };
            context.Payments.Add(payment2);
            context.Entry(payment2).Property("Id").CurrentValue = Guid.Parse("b2222222-2222-2222-2222-222222222222");

            await context.SaveChangesAsync();

            Logger.Information("✅ [Payment] Seed data inserted: 2 payments");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ [Payment] Failed to seed data");
            throw;
        }
    }
}
