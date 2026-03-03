using Microsoft.EntityFrameworkCore;
using Sample.Order.NetFramework481.Domain.Customers;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sample.Order.NetFramework481.Infrastructure.Persistence;

public static class DbInitializer
{
    private static readonly ILogger Logger = Log.ForContext(typeof(DbInitializer));

    public static async Task InitializeAsync(OrderDbContext context)
    {
        try
        {
            Logger.Information("🔧 [Order] Initializing database...");

            // Wait for SQL Server to be ready (retry logic)
            var maxRetries = 10;
            var retryDelay = TimeSpan.FromSeconds(3);

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (await context.Database.CanConnectAsync())
                    {
                        Logger.Information("✅ [Order] Successfully connected to database");
                        break;
                    }
                }
                catch (Exception)
                {
                    if (i == maxRetries - 1)
                    {
                        Logger.Error("❌ [Order] Failed to connect to database after {Retries} retries", maxRetries);
                        throw;
                    }

                    Logger.Warning("⚠️ [Order] SQL Server not ready, waiting {Delay}s... (Attempt {Current}/{Max})", 
                        retryDelay.TotalSeconds, i + 1, maxRetries);
                    await Task.Delay(retryDelay);
                }
            }

            // Check pending migrations
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToList();

            Logger.Information($"📊 [Order] Pending migrations: {pendingMigrations.Count}");
            Logger.Information($"📊 [Order] Applied migrations: {appliedMigrations.Count}");

            if (pendingMigrations.Any())
            {
                Logger.Information($"⚙️ [Order] Applying {pendingMigrations.Count} pending migrations...");
                foreach (var migration in pendingMigrations)
                {
                    Logger.Information($"  - {migration}");
                }
                await context.Database.MigrateAsync();
                Logger.Information("✅ [Order] Database migrations applied");
            }
            else if (appliedMigrations.Any())
            {
                Logger.Information("✅ [Order] Database is up to date");
            }
            else
            {
                Logger.Warning("⚠️ [Order] No migrations found! Ensure migrations assembly is configured correctly.");
                Logger.Information("⚙️ [Order] Creating database using EnsureCreated as fallback...");
                await context.Database.EnsureCreatedAsync();
            }

            await SeedDataAsync(context);
            Logger.Information("✅ [Order] Database initialization completed");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ [Order] Database initialization failed");
            throw;
        }
    }

    private static async Task SeedDataAsync(OrderDbContext context)
    {
        try
        {
            // Check if tables exist and are accessible
            var canConnect = await context.Database.CanConnectAsync();
            if (!canConnect)
            {
                Logger.Error("❌ [Order] Cannot connect to database");
                throw new InvalidOperationException("Cannot connect to database");
            }

            Logger.Information("✅ [Order] Database connection established");

            // Check if data already exists
            if (await context.Customers.AnyAsync())
            {
                Logger.Information("⏭️ [Order] Database already seeded, skipping seed data");
                return;
            }

            Logger.Information("🌱 [Order] Seeding initial data...");

            var customerId1 = Guid.Parse("62c8cbf8-d0fd-4bac-b2d8-03c1ce2460ae");
            var customerId2 = Guid.NewGuid();

            // Create customers using EF Core entities
            var customer1 = new Customer
            {
                Name = "John Doe",
                Email = "john.doe@example.com",
                Phone = "+1234567890"
            };
            context.Customers.Add(customer1);
            context.Entry(customer1).Property("Id").CurrentValue = customerId1;

            var customer2 = new Customer
            {
                Name = "Jane Smith",
                Email = "jane.smith@example.com",
                Phone = "+1234567891"
            };
            context.Customers.Add(customer2);
            context.Entry(customer2).Property("Id").CurrentValue = customerId2;

            // Create addresses
            var address1 = new Address
            {
                CustomerId = customerId1,
                Street = "123 Main St",
                City = "New York",
                State = "NY",
                ZipCode = "10001",
                Country = "USA",
                IsDefault = true
            };
            context.Addresses.Add(address1);
            context.Entry(address1).Property("Id").CurrentValue = Guid.Parse("9f699b2e-7e0b-4d75-9b01-f678d13771f9");

            var address2 = new Address
            {
                CustomerId = customerId2,
                Street = "456 Oak Ave",
                City = "Los Angeles",
                State = "CA",
                ZipCode = "90001",
                Country = "USA",
                IsDefault = true
            };
            context.Addresses.Add(address2);

            // Create cards
            var card1 = new Card
            {
                CustomerId = customerId1,
                CardHolderName = "John Doe",
                Last4Digits = "1234",
                ExpiryMonth = 12,
                ExpiryYear = 2025,
                IsDefault = true
            };
            context.Cards.Add(card1);
            context.Entry(card1).Property("Id").CurrentValue = Guid.Parse("54616a07-ce09-40e2-b8e7-5d44b0031350");

            var card2 = new Card
            {
                CustomerId = customerId2,
                CardHolderName = "Jane Smith",
                Last4Digits = "5678",
                ExpiryMonth = 6,
                ExpiryYear = 2026,
                IsDefault = true
            };
            context.Cards.Add(card2);

            await context.SaveChangesAsync();

            Logger.Information("✅ [Order] Seed data inserted: 2 customers with addresses and cards");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ [Order] Failed to seed data");
            throw;
        }
    }
}
