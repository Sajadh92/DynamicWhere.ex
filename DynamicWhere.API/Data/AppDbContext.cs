using DynamicWhere.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // DbSets
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Product Configuration
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Price).HasPrecision(18, 2);
            entity.Property(e => e.Rating).HasPrecision(3, 2);

            // JSON columns
            entity.Property(e => e.Specifications).HasColumnType("jsonb");
            entity.Property(e => e.Tags).HasColumnType("jsonb");
            entity.Property(e => e.Dimensions).HasColumnType("jsonb");

            entity.HasOne(e => e.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.IsActive);
        });

        // Category Configuration
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);

            // JSON column
            entity.Property(e => e.Metadata).HasColumnType("jsonb");

            entity.HasOne(e => e.ParentCategory)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(e => e.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.Name);
        });

        // Customer Configuration
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.TotalSpent).HasPrecision(18, 2);

            // Owned complex type
            entity.OwnsOne(e => e.ContactInfo, contact =>
            {
                contact.Property(c => c.Email).IsRequired().HasMaxLength(200);
                contact.Property(c => c.PhoneNumber).HasMaxLength(20);
                contact.Property(c => c.AlternateEmail).HasMaxLength(200);
                contact.Property(c => c.AlternatePhone).HasMaxLength(20);

                // JSON column
                contact.Property(c => c.SocialMediaLinks).HasColumnType("jsonb");
            });

            // JSON columns
            entity.Property(e => e.Addresses).HasColumnType("jsonb");
            entity.Property(e => e.Preferences).HasColumnType("jsonb");
            entity.Property(e => e.LoyaltyPoints).HasColumnType("jsonb");

            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.IsActive);
        });

        // Order Configuration
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.SubTotal).HasPrecision(18, 2);
            entity.Property(e => e.TaxAmount).HasPrecision(18, 2);
            entity.Property(e => e.ShippingCost).HasPrecision(18, 2);
            entity.Property(e => e.DiscountAmount).HasPrecision(18, 2);
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);

            // Store enums as strings for better text filtering support
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.PaymentMethod).HasConversion<string>();

            // Owned complex types
            entity.OwnsOne(e => e.ShippingAddress, address =>
            {
                address.Property(a => a.Street).HasMaxLength(200);
                address.Property(a => a.City).HasMaxLength(100);
                address.Property(a => a.State).HasMaxLength(100);
                address.Property(a => a.Country).HasMaxLength(100);
                address.Property(a => a.ZipCode).HasMaxLength(20);
            });

            entity.OwnsOne(e => e.BillingAddress, address =>
            {
                address.Property(a => a.Street).HasMaxLength(200);
                address.Property(a => a.City).HasMaxLength(100);
                address.Property(a => a.State).HasMaxLength(100);
                address.Property(a => a.Country).HasMaxLength(100);
                address.Property(a => a.ZipCode).HasMaxLength(20);
            });

            // JSON columns
            entity.Property(e => e.TrackingInfo).HasColumnType("jsonb");
            entity.Property(e => e.Notes).HasColumnType("jsonb");

            entity.HasOne(e => e.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.OrderNumber).IsUnique();
            entity.HasIndex(e => e.OrderDate);
            entity.HasIndex(e => e.Status);
        });

        // OrderItem Configuration
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.Discount).HasPrecision(18, 2);
            entity.Property(e => e.TotalPrice).HasPrecision(18, 2);

            // JSON column
            entity.Property(e => e.CustomizationOptions).HasColumnType("jsonb");

            entity.HasOne(e => e.Order)
                .WithMany(o => o.OrderItems)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Product)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Review Configuration
        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Content).IsRequired().HasMaxLength(2000);

            // JSON columns
            entity.Property(e => e.ImageUrls).HasColumnType("jsonb");
            entity.Property(e => e.Details).HasColumnType("jsonb");

            entity.HasOne(e => e.Customer)
                .WithMany(c => c.Reviews)
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Product)
                .WithMany(p => p.Reviews)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ParentReview)
                .WithMany(r => r.Replies)
                .HasForeignKey(e => e.ParentReviewId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.Rating);
            entity.HasIndex(e => e.CreatedAt);
        });

        // Employee Configuration
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.Property(e => e.EmployeeCode).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Salary).HasPrecision(18, 2);
            entity.Property(e => e.Department).HasMaxLength(100);
            entity.Property(e => e.Position).HasMaxLength(100);

            // Owned complex type
            entity.OwnsOne(e => e.Address, address =>
            {
                address.Property(a => a.Street).HasMaxLength(200);
                address.Property(a => a.City).HasMaxLength(100);
                address.Property(a => a.State).HasMaxLength(100);
                address.Property(a => a.Country).HasMaxLength(100);
                address.Property(a => a.ZipCode).HasMaxLength(20);
            });

            // JSON columns
            entity.Property(e => e.Skills).HasColumnType("jsonb");
            entity.Property(e => e.Certifications).HasColumnType("jsonb");
            entity.Property(e => e.WorkSchedule).HasColumnType("jsonb");
            entity.Property(e => e.EmergencyContacts).HasColumnType("jsonb");

            entity.HasOne(e => e.Manager)
                .WithMany(m => m.Subordinates)
                .HasForeignKey(e => e.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.EmployeeCode).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Department);
        });
    }
}
