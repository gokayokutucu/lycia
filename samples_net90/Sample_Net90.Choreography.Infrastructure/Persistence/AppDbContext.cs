using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sample_Net90.Choreography.Domain.Entities;


namespace Sample_Net90.Choreography.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    //dotnet ef database drop --project ../Sample_Net90.Choreography.Infrastructure --startup-project . --force
    //dotnet ef migrations add AutoMigration --project ../Sample_Net90.Choreography.Infrastructure --startup-project .
    //dotnet ef database update --project ../Sample_Net90.Choreography.Infrastructure --startup-project .

    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            optionsBuilder.UseSqlServer(connectionString);
        }
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(entity.ClrType.Name);
        }

        // Customer -> Address
        modelBuilder.Entity<Address>()
            .HasOne(a => a.Customer)
            .WithMany(c => c.Addresses)
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Customer -> Card
        modelBuilder.Entity<Card>()
            .HasOne(c => c.Customer)
            .WithMany(cu => cu.Cards)
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Customer -> Order
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Order -> DeliveryAddress
        modelBuilder.Entity<Order>()
            .HasOne(o => o.DeliveryAddress)
            .WithMany()
            .HasForeignKey(o => o.DeliveryAddressId)
            .OnDelete(DeleteBehavior.Restrict);

        // Order -> BillingAddress
        modelBuilder.Entity<Order>()
            .HasOne(o => o.BillingAddress)
            .WithMany()
            .HasForeignKey(o => o.BillingAddressId)
            .OnDelete(DeleteBehavior.Restrict);

        // Order -> CartItem (Products)
        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.Order)
            .WithMany(o => o.Products)
            .HasForeignKey(ci => ci.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // CartItem -> Product
        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.Product)
            .WithMany()
            .HasForeignKey(ci => ci.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Payment -> Order
        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Order)
            .WithMany()
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Payment -> Card
        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Card)
            .WithMany()
            .HasForeignKey(p => p.CardId)
            .OnDelete(DeleteBehavior.Restrict);

        // Shipment -> Order
        modelBuilder.Entity<Shipment>()
            .HasOne(s => s.Order)
            .WithMany()
            .HasForeignKey(s => s.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Shipment -> DeliveryAddress
        modelBuilder.Entity<Shipment>()
            .HasOne(s => s.DeliveryAddress)
            .WithMany()
            .HasForeignKey(s => s.DeliveryAddressId)
            .OnDelete(DeleteBehavior.Restrict);

        // Shipment -> BillingAddress
        modelBuilder.Entity<Shipment>()
            .HasOne(s => s.BillingAddress)
            .WithMany()
            .HasForeignKey(s => s.BillingAddressId)
            .OnDelete(DeleteBehavior.Restrict);

        // Product -> Stock (1:1)
        modelBuilder.Entity<Product>()
            .HasOne(p => p.Stock)
            .WithOne(s => s.Product)
            .HasForeignKey<Stock>(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Payment>()
            .Property(p => p.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Customer>().HasKey(c => c.CustomerId);
        modelBuilder.Entity<Address>().HasKey(a => a.AddressId);
        modelBuilder.Entity<Card>().HasKey(c => c.CardId);
        modelBuilder.Entity<Product>().HasKey(p => p.ProductId);
        modelBuilder.Entity<Stock>().HasKey(s => s.StockId);
        modelBuilder.Entity<CartItem>().HasKey(ci => ci.CartItemId);
        modelBuilder.Entity<Order>().HasKey(o => o.OrderId);
        modelBuilder.Entity<Payment>().HasKey(p => p.PaymentId);
        modelBuilder.Entity<Shipment>().HasKey(s => s.ShipmentId);
    }
}
