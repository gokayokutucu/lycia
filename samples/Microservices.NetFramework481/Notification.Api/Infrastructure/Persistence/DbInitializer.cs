using Microsoft.EntityFrameworkCore;
using Sample.Notification.NetFramework481.Domain.Notifications;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sample.Notification.NetFramework481.Infrastructure.Persistence;

public static class DbInitializer
{
    private static readonly ILogger Logger = Log.ForContext(typeof(DbInitializer));

    public static async Task InitializeAsync(NotificationDbContext context)
    {
        try
        {
            Logger.Information("🔧 [Notification] Initializing database...");

            // Wait for SQL Server to be ready (retry logic)
            var maxRetries = 10;
            var retryDelay = TimeSpan.FromSeconds(3);

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (await context.Database.CanConnectAsync())
                    {
                        Logger.Information("✅ [Notification] Successfully connected to database");
                        break;
                    }
                }
                catch (Exception)
                {
                    if (i == maxRetries - 1)
                    {
                        Logger.Error("❌ [Notification] Failed to connect to database after {Retries} retries", maxRetries);
                        throw;
                    }

                    Logger.Warning("⚠️ [Notification] SQL Server not ready, waiting {Delay}s... (Attempt {Current}/{Max})", 
                        retryDelay.TotalSeconds, i + 1, maxRetries);
                    await Task.Delay(retryDelay);
                }
            }

            // Check pending migrations
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            
            Logger.Information($"📊 [Notification] Pending migrations: {pendingMigrations.Count}");
            Logger.Information($"📊 [Notification] Applied migrations: {appliedMigrations.Count}");
            
            if (pendingMigrations.Any())
            {
                Logger.Information($"⚙️ [Notification] Applying {pendingMigrations.Count} pending migrations...");
                foreach (var migration in pendingMigrations)
                {
                    Logger.Information($"  - {migration}");
                }
                await context.Database.MigrateAsync();
                Logger.Information("✅ [Notification] Database migrations applied");
            }
            else if (appliedMigrations.Any())
            {
                Logger.Information("✅ [Notification] Database is up to date");
            }
            else
            {
                Logger.Warning("⚠️ [Notification] No migrations found! Ensure migrations assembly is configured correctly.");
                Logger.Information("⚙️ [Notification] Creating database using EnsureCreated as fallback...");
                await context.Database.EnsureCreatedAsync();
            }

            await SeedDataAsync(context);
            Logger.Information("✅ [Notification] Database initialization completed");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ [Notification] Database initialization failed");
            throw;
        }
    }

    private static async Task SeedDataAsync(NotificationDbContext context)
    {
        try
        {
            // Check if tables exist and are accessible
            var canConnect = await context.Database.CanConnectAsync();
            if (!canConnect)
            {
                Logger.Error("❌ [Notification] Cannot connect to database");
                throw new InvalidOperationException("Cannot connect to database");
            }

            Logger.Information("✅ [Notification] Database connection established");

            // Check if data already exists
            if (await context.Notifications.AnyAsync())
            {
                Logger.Information("⏭️ [Notification] Database already seeded, skipping seed data");
                return;
            }

            Logger.Information("🌱 [Notification] Seeding initial data...");

            // Create sample notifications using EF Core entities
            var notification1 = new Domain.Notifications.Notification
            {
                Recipient = "john.doe@example.com",
                Type = NotificationType.Email,
                Subject = "Order Confirmation",
                Message = "Your order has been confirmed and is being processed.",
                Status = NotificationStatus.Sent,
                RelatedEntityType = "Order",
                RelatedEntityId = Guid.Parse("62c8cbf8-d0fd-4bac-b2d8-03c1ce2460ae"),
                SentAt = DateTime.UtcNow
            };
            context.Notifications.Add(notification1);
            context.Entry(notification1).Property("Id").CurrentValue = Guid.Parse("a1111111-1111-1111-1111-111111111111");

            var notification2 = new Domain.Notifications.Notification
            {
                Recipient = "+1234567891",
                Type = NotificationType.SMS,
                Subject = "Payment Received",
                Message = "We have received your payment. Thank you!",
                Status = NotificationStatus.Pending,
                RelatedEntityType = "Payment",
                RelatedEntityId = Guid.NewGuid()
            };
            context.Notifications.Add(notification2);
            context.Entry(notification2).Property("Id").CurrentValue = Guid.Parse("a2222222-2222-2222-2222-222222222222");

            await context.SaveChangesAsync();

            Logger.Information("✅ [Notification] Seed data inserted: 2 notifications");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ [Notification] Failed to seed data");
            throw;
        }
    }
}
