using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DynamicWhere.Tests.Domain;

/// <summary>
/// SQLite-backed context for the sales domain. Enums are stored as strings, matching the
/// production configuration the library documents for <c>DataType.Enum</c>.
/// </summary>
public class SalesContext : DbContext
{
    public SalesContext(DbContextOptions<SalesContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasOne(e => e.ParentCategory)
                  .WithMany(e => e.SubCategories)
                  .HasForeignKey(e => e.ParentCategoryId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasOne(e => e.Category)
                  .WithMany(e => e.Products)
                  .HasForeignKey(e => e.CategoryId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            // Enums as strings so DataType.Enum conditions compare against text.
            entity.Property(e => e.Gender).HasConversion<string>();
            entity.Property(e => e.Tier).HasConversion<string>();

            entity.OwnsOne(e => e.ContactInfo);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.PaymentMethod).HasConversion<string>();

            entity.OwnsOne(e => e.ShippingAddress);

            entity.HasOne(e => e.Customer)
                  .WithMany(e => e.Orders)
                  .HasForeignKey(e => e.CustomerId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasOne(e => e.Order)
                  .WithMany(e => e.OrderItems)
                  .HasForeignKey(e => e.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Product)
                  .WithMany(e => e.OrderItems)
                  .HasForeignKey(e => e.ProductId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasOne(e => e.Customer)
                  .WithMany(e => e.Reviews)
                  .HasForeignKey(e => e.CustomerId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Product)
                  .WithMany(e => e.Reviews)
                  .HasForeignKey(e => e.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        StoreDecimalsAsDouble(modelBuilder);
    }

    /// <summary>
    /// SQLite has no native decimal type — it stores decimals as TEXT, and the provider then refuses
    /// <c>SUM</c>/<c>AVG</c>/<c>MIN</c>/<c>MAX</c> over them ("SQLite cannot apply aggregate operator
    /// 'Sum' on expressions of type 'decimal'"). Converting to REAL lets the aggregation tests run the
    /// same way they would on SQL Server or PostgreSQL. This is a test-host concern only: the CLR
    /// properties stay <see cref="decimal"/>, so the library sees exactly the production types.
    /// </summary>
    private static void StoreDecimalsAsDouble(ModelBuilder modelBuilder)
    {
        var toDouble = new ValueConverter<decimal, double>(v => (double)v, v => (decimal)v);
        var toNullableDouble = new ValueConverter<decimal?, double?>(v => (double?)v, v => (decimal?)v);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(decimal))
            {
                property.SetValueConverter(toDouble);
            }
            else if (property.ClrType == typeof(decimal?))
            {
                property.SetValueConverter(toNullableDouble);
            }
        }
    }
}
