using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;
using ProductEntity = Sample.Product.NetFramework481.Domain.Products.Product;

namespace Sample.Product.NetFramework481.Infrastructure.Persistence;

public static class DbInitializer
{
    private static readonly ILogger Logger = Log.ForContext(typeof(DbInitializer));

    public static async Task InitializeAsync(ProductDbContext context)
    {
        try
        {
            Logger.Information("🔧 [Product] Initializing database...");

            // Wait for SQL Server to be ready (retry logic)
            var maxRetries = 10;
            var retryDelay = TimeSpan.FromSeconds(3);

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (await context.Database.CanConnectAsync())
                    {
                        Logger.Information("✅ [Product] Successfully connected to database");
                        break;
                    }
                }
                catch (Exception)
                {
                    if (i == maxRetries - 1)
                    {
                        Logger.Error("❌ [Product] Failed to connect to database after {Retries} retries", maxRetries);
                        throw;
                    }

                    Logger.Warning("⚠️ [Product] SQL Server not ready, waiting {Delay}s... (Attempt {Current}/{Max})", 
                        retryDelay.TotalSeconds, i + 1, maxRetries);
                    await Task.Delay(retryDelay);
                }
            }

            // Check pending migrations
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            
            Logger.Information($"📊 [Product] Pending migrations: {pendingMigrations.Count}");
            Logger.Information($"📊 [Product] Applied migrations: {appliedMigrations.Count}");
            
            if (pendingMigrations.Any())
            {
                Logger.Information($"⚙️ [Product] Applying {pendingMigrations.Count} pending migrations...");
                foreach (var migration in pendingMigrations)
                {
                    Logger.Information($"  - {migration}");
                }
                await context.Database.MigrateAsync();
                Logger.Information("✅ [Product] Database migrations applied");
            }
            else if (appliedMigrations.Any())
            {
                Logger.Information("✅ [Product] Database is up to date");
            }
            else
            {
                Logger.Warning("⚠️ [Product] No migrations found! Ensure migrations assembly is configured correctly.");
                Logger.Information("⚙️ [Product] Creating database using EnsureCreated as fallback...");
                await context.Database.EnsureCreatedAsync();
            }

            await SeedDataAsync(context);
            Logger.Information("✅ [Product] Database initialization completed");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ [Product] Database initialization failed");
            throw;
        }
    }

    private static async Task SeedDataAsync(ProductDbContext context)
    {
        try
        {
            // Check if tables exist and are accessible
            var canConnect = await context.Database.CanConnectAsync();
            if (!canConnect)
            {
                Logger.Error("❌ [Product] Cannot connect to database");
                throw new InvalidOperationException("Cannot connect to database");
            }

            Logger.Information("✅ [Product] Database connection established");

            // Check if data already exists
            if (await context.Products.AnyAsync())
            {
                Logger.Information("⏭️ [Product] Database already seeded, skipping seed data");
                return;
            }

            Logger.Information("🌱 [Product] Seeding initial data...");

            // Create products using EF Core entities
            var product1 = new ProductEntity
            {
                Name = "Laptop Pro 15",
                Price = 999.99m,
                StockQuantity = 50,
                ReservedQuantity = 0
            };
            context.Products.Add(product1);
            context.Entry(product1).Property("Id").CurrentValue = Guid.Parse("11111111-1111-1111-1111-111111111111");

            var product2 = new ProductEntity
            {
                Name = "Wireless Mouse",
                Price = 29.99m,
                StockQuantity = 200,
                ReservedQuantity = 0
            };
            context.Products.Add(product2);
            context.Entry(product2).Property("Id").CurrentValue = Guid.Parse("22222222-2222-2222-2222-222222222222");

            var product3 = new ProductEntity
            {
                Name = "USB-C Hub",
                Price = 49.99m,
                StockQuantity = 150,
                ReservedQuantity = 0
            };
            context.Products.Add(product3);
            context.Entry(product3).Property("Id").CurrentValue = Guid.Parse("33333333-3333-3333-3333-333333333333");

            await context.SaveChangesAsync();

            Logger.Information("✅ [Product] Seed data inserted: 3 products");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ [Product] Failed to seed data");
            throw;
        }
    }
}
