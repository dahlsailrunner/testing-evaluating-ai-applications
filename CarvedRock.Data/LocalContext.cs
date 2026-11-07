using CarvedRock.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CarvedRock.Data;

[ExcludeFromCodeCoverage]
public class LocalContext(DbContextOptions<LocalContext> options) : DbContext(options)
{
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<CartItem> CartItems { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderDetail> OrderDetails { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.HasIndex(c => c.UserId);
            // one row per product per user, enabling a simple upsert by (UserId, ProductId)
            entity.HasIndex(c => new { c.UserId, c.ProductId }).IsUnique();
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasIndex(o => o.UserId);
            entity.HasMany(o => o.Details)
                  .WithOne(d => d.Order)
                  .HasForeignKey(d => d.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public void MigrateAndCreateData(bool force = false)
    {
        var pgConn = new NpgsqlConnectionStringBuilder(Database.GetConnectionString());
        if (!force && pgConn != null && 
                              (string.Equals(pgConn.Host, "testing")
                            || string.Equals(pgConn.Host, "127.0.0.1")
                            || string.Equals(pgConn.Host, "NOT_USED")))
            return;  // during test runs this will be handled separately

        Database.EnsureCreated(); // always apply migrations       

        if (!force && pgConn != null &&
            !string.Equals(pgConn.Host, "localhost", StringComparison.InvariantCultureIgnoreCase) &&
            !string.Equals(pgConn.Host, "postgres", StringComparison.InvariantCultureIgnoreCase))
            return;  // only seed/refresh data if we're connecting to a local database

        if (Products.Any())
        {
            Products.RemoveRange(Products);
            SaveChanges();
        }
        ;

        // Can you help me generate some data for Product?  I'd like 50 products,
        // and each of them should be something that might be found at an outdoor 
        // recreational equipment store.  The categories for them should be one of  
        // "boots", "equipment", and "kayaks". Image Urls should be something from 
        // the service picsum.photos. Use JSON format and exclude the ID property.
        string baseDirectory = AppContext.BaseDirectory;
        string jsonString = File.ReadAllText(Path.Combine(baseDirectory, "SeedData.json"));
        var products = JsonSerializer.Deserialize<List<Product>>(jsonString,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (products != null)
        {
            Products.AddRange(products);
            SaveChanges();
        }

        SaveChanges();
    }
}
