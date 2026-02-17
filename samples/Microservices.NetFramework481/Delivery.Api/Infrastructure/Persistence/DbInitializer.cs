using Microsoft.EntityFrameworkCore;
using Sample.Delivery.NetFramework481.Domain.Deliveries;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sample.Delivery.NetFramework481.Infrastructure.Persistence;

public static class DbInitializer
{
    private static readonly ILogger Logger = Log.ForContext(typeof(DbInitializer));

    public static async Task InitializeAsync(DeliveryDbContext context)
    {
        try
        {
            Logger.Information("🔧 [Delivery] Initializing database...");

            // Wait for SQL Server to be ready (retry logic)
            var maxRetries = 10;
            var retryDelay = TimeSpan.FromSeconds(3);

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (await context.Database.CanConnectAsync())
                    {
                        Logger.Information("✅ [Delivery] Successfully connected to database");
                        break;
                    }
                }
                catch (Exception)
                {
                    if (i == maxRetries - 1)
                    {
                        Logger.Error("❌ [Delivery] Failed to connect to database after {Retries} retries", maxRetries);
                        throw;
                    }

                    Logger.Warning("⚠️ [Delivery] SQL Server not ready, waiting {Delay}s... (Attempt {Current}/{Max})", 
                        retryDelay.TotalSeconds, i + 1, maxRetries);
                    await Task.Delay(retryDelay);
                }
            }

            // Check pending migrations
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            
            Logger.Information($"📊 [Delivery] Pending migrations: {pendingMigrations.Count}");
            Logger.Information($"📊 [Delivery] Applied migrations: {appliedMigrations.Count}");
            
            if (pendingMigrations.Any())
            {
                Logger.Information($"⚙️ [Delivery] Applying {pendingMigrations.Count} pending migrations...");
                foreach (var migration in pendingMigrations)
                {
                    Logger.Information($"  - {migration}");
                }
                await context.Database.MigrateAsync();
                Logger.Information("✅ [Delivery] Database migrations applied");
            }
            else if (appliedMigrations.Any())
            {
                Logger.Information("✅ [Delivery] Database is up to date");
            }
            else
            {
                Logger.Warning("⚠️ [Delivery] No migrations found! Ensure migrations assembly is configured correctly.");
                Logger.Information("⚙️ [Delivery] Creating database using EnsureCreated as fallback...");
                await context.Database.EnsureCreatedAsync();
            }

            await SeedDataAsync(context);
            Logger.Information("✅ [Delivery] Database initialization completed");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ [Delivery] Database initialization failed");
            throw;
        }
    }

    private static async Task SeedDataAsync(DeliveryDbContext context)
    {
        try
        {
            // Check if tables exist and are accessible
            var canConnect = await context.Database.CanConnectAsync();
            if (!canConnect)
            {
                Logger.Error("❌ [Delivery] Cannot connect to database");
                throw new InvalidOperationException("Cannot connect to database");
            }

            Logger.Information("✅ [Delivery] Database connection established");

            // Check if data already exists
            if (await context.Deliveries.AnyAsync())
            {
                Logger.Information("⏭️ [Delivery] Database already seeded, skipping seed data");
                return;
            }

            Logger.Information("🌱 [Delivery] Seeding initial data...");

            // Create sample deliveries using EF Core entities
            var delivery1 = new Domain.Deliveries.Delivery
            {
                OrderId = Guid.Parse("62c8cbf8-d0fd-4bac-b2d8-03c1ce2460ae"),
                CustomerName = "John Doe",
                ShippingStreet = "123 Main St",
                ShippingCity = "New York",
                ShippingState = "NY",
                ShippingZipCode = "10001",
                ShippingCountry = "USA",
                Status = Domain.Deliveries.DeliveryStatus.Pending,
                TrackingNumber = Guid.NewGuid().ToString("N").Substring(0, 12).ToUpper()
            };
            context.Deliveries.Add(delivery1);
            context.Entry(delivery1).Property("Id").CurrentValue = Guid.Parse("d1111111-1111-1111-1111-111111111111");

            var delivery2 = new Domain.Deliveries.Delivery
            {
                OrderId = Guid.NewGuid(),
                CustomerName = "Jane Smith",
                ShippingStreet = "456 Oak Ave",
                ShippingCity = "Los Angeles",
                ShippingState = "CA",
                ShippingZipCode = "90001",
                ShippingCountry = "USA",
                Status = Domain.Deliveries.DeliveryStatus.InTransit,
                TrackingNumber = Guid.NewGuid().ToString("N").Substring(0, 12).ToUpper()
            };
            context.Deliveries.Add(delivery2);
            context.Entry(delivery2).Property("Id").CurrentValue = Guid.Parse("d2222222-2222-2222-2222-222222222222");

            await context.SaveChangesAsync();

            Logger.Information("✅ [Delivery] Seed data inserted: 2 deliveries");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ [Delivery] Failed to seed data");
            throw;
        }
    }
}
