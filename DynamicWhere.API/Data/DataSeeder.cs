using DynamicWhere.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DynamicWhere.API.Data;

public static class DataSeeder
{
    public static async Task SeedDataAsync(AppDbContext context)
    {
        // Clear existing data
        await context.Categories.ExecuteDeleteAsync();
        await context.Reviews.ExecuteDeleteAsync();
        await context.OrderItems.ExecuteDeleteAsync();
        await context.Orders.ExecuteDeleteAsync();
        await context.Products.ExecuteDeleteAsync();
        await context.Customers.ExecuteDeleteAsync();
        await context.Employees.ExecuteDeleteAsync();

        // Seed Categories
        var electronics = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Electronics",
            Description = "Electronic devices and accessories",
            DisplayOrder = 1,
            IsActive = true,
            Metadata = new Dictionary<string, string>
            {
                { "Icon", "electronics-icon.png" },
                { "Color", "#007bff" }
            }
        };

        var smartphones = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Smartphones",
            Description = "Mobile phones and accessories",
            DisplayOrder = 1,
            IsActive = true,
            ParentCategoryId = electronics.Id,
            Metadata = new Dictionary<string, string>
            {
                { "Icon", "phone-icon.png" },
                { "Featured", "true" }
            }
        };

        var laptops = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Laptops",
            Description = "Portable computers",
            DisplayOrder = 2,
            IsActive = true,
            ParentCategoryId = electronics.Id
        };

        context.Categories.AddRange(electronics, smartphones, laptops);

        await context.SaveChangesAsync();

        // Seed Products
        var products = new List<Product>
        {
            new() {
                Id = Guid.NewGuid(),
                Name = "iPhone 15 Pro",
                Description = "Latest flagship smartphone from Apple",
                Price = 999.99m,
                Rating = 4.8,
                StockQuantity = 50,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-30).ToUniversalTime(),
                ManufactureDate = new DateOnly(2023, 9, 15),
                CategoryId = smartphones.Id,
                Tags = [ "apple", "premium", "5g", "ios" ],
                Dimensions = new Dictionary<string, double>
                {
                    { "height", 146.6 },
                    { "width", 70.6 },
                    { "depth", 8.25 },
                    { "weight", 187 }
                },
                Specifications = JsonDocument.Parse("""
                {
                  "display": "6.1 inch OLED",
                  "processor": "A17 Pro",
                  "ram": "8GB",
                  "storage": "256GB",
                  "camera": "48MP main + 12MP ultrawide + 12MP telephoto",
                  "battery": "3274 mAh"
                }
                """)
            },
            new() {
                Id = Guid.NewGuid(),
                Name = "Samsung Galaxy S24",
                Description = "Premium Android smartphone",
                Price = 899.99m,
                Rating = 4.7,
                StockQuantity = 75,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-25).ToUniversalTime(),
                ManufactureDate = new DateOnly(2024, 1, 10),
                CategoryId = smartphones.Id,
                Tags = [ "samsung", "android", "5g", "flagship" ],
                Dimensions = new Dictionary<string, double>
                {
                    { "height", 147 },
                    { "width", 70.6 },
                    { "depth", 7.6 },
                    { "weight", 168 }
                }
            },
            new() {
                Id = Guid.NewGuid(),
                Name = "MacBook Pro 16\"",
                Description = "Powerful laptop for professionals",
                Price = 2499.99m,
                Rating = 4.9,
                StockQuantity = 30,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-45).ToUniversalTime(),
                ManufactureDate = new DateOnly(2023, 11, 1),
                CategoryId = null, 
                Tags = [ "apple", "laptop", "professional", "m3" ],
                Dimensions = new Dictionary<string, double>
                {
                    { "height", 1.68 },
                    { "width", 35.57 },
                    { "depth", 24.81 },
                    { "weight", 2160 }
                },
                Specifications = JsonDocument.Parse("""
                {
                  "display": "16.2 inch Liquid Retina XDR",
                  "processor": "M3 Pro",
                  "ram": "18GB",
                  "storage": "512GB SSD",
                  "graphics": "18-core GPU",
                  "battery": "100Wh"
                }
                """)
            }
        };

        context.Products.AddRange(products);

        await context.SaveChangesAsync();

        // Seed Customers
        var customers = new List<Customer>
        {
            new() {
                Id = Guid.NewGuid(),
                FirstName = "John",
                LastName = "Doe",
                Username = "johndoe",
                DateOfBirth = DateTime.SpecifyKind(new DateTime(1990, 5, 15), DateTimeKind.Utc),
                Gender = Gender.Male,
                IsActive = true,
                RegisteredAt = DateTime.UtcNow.AddMonths(-6).ToUniversalTime(),
                LastLoginAt = DateTime.UtcNow.AddHours(-2).ToUniversalTime(),
                TotalSpent = 1500.50m,
                Tier = CustomerTier.Gold,
                ContactInfo = new ContactInfo
                {
                    Email = "john.doe@example.com",
                    PhoneNumber = "+1-555-0101",
                    AlternateEmail = "j.doe@work.com",
                    SocialMediaLinks =
                    [
                        "https://twitter.com/johndoe",
                        "https://linkedin.com/in/johndoe"
                    ]
                },
                Addresses =
                [
                    new Address
                    {
                        Street = "123 Main Street",
                        City = "New York",
                        State = "NY",
                        Country = "USA",
                        ZipCode = "10001",
                        Latitude = 40.7128,
                        Longitude = -74.0060
                    },
                    new Address
                    {
                        Street = "456 Work Ave",
                        City = "New York",
                        State = "NY",
                        Country = "USA",
                        ZipCode = "10002"
                    }
                ],
                LoyaltyPoints =
                [
                    new LoyaltyPoint
                    {
                        Points = 500,
                        EarnedDate = DateTime.UtcNow.AddMonths(-3).ToUniversalTime(),
                        ExpiryDate = DateTime.UtcNow.AddMonths(9).ToUniversalTime(),
                        Source = "Purchase",
                        IsRedeemed = false
                    },
                    new LoyaltyPoint
                    {
                        Points = 100,
                        EarnedDate = DateTime.UtcNow.AddMonths(-2).ToUniversalTime(),
                        ExpiryDate = DateTime.UtcNow.AddMonths(10).ToUniversalTime(),
                        Source = "Referral",
                        IsRedeemed = false
                    }
                ],
                Preferences = JsonDocument.Parse("""
                {
                  "newsletter": true,
                  "smsNotifications": false,
                  "favoriteCategories": ["electronics", "books"],
                  "language": "en",
                  "currency": "USD"
                }
                """)
            },
            new() {
                Id = Guid.NewGuid(),
                FirstName = "Jane",
                LastName = "Smith",
                Username = "janesmith",
                DateOfBirth = DateTime.SpecifyKind(new DateTime(1985, 8, 22), DateTimeKind.Utc),
                Gender = Gender.Female,
                IsActive = true,
                RegisteredAt = DateTime.UtcNow.AddMonths(-12).ToUniversalTime(),
                LastLoginAt = DateTime.UtcNow.AddDays(-1).ToUniversalTime(),
                TotalSpent = 3200.75m,
                Tier = CustomerTier.Platinum,
                ContactInfo = new ContactInfo
                {
                    Email = "jane.smith@example.com",
                    PhoneNumber = "+1-555-0202"
                },
                Addresses =
                [
                    new Address
                    {
                        Street = "789 Oak Boulevard",
                        City = "Los Angeles",
                        State = "CA",
                        Country = "USA",
                        ZipCode = "90001",
                        Latitude = 34.0522,
                        Longitude = -118.2437
                    }
                ],
                LoyaltyPoints =
                [
                    new LoyaltyPoint
                    {
                        Points = 1000,
                        EarnedDate = DateTime.UtcNow.AddMonths(-6).ToUniversalTime(),
                        ExpiryDate = DateTime.UtcNow.AddMonths(6).ToUniversalTime(),
                        Source = "Purchase",
                        IsRedeemed = false
                    }
                ]
            }
        };

        context.Customers.AddRange(customers);

        await context.SaveChangesAsync();

        // Seed Employees
        var ceo = new Employee
        {
            Id = Guid.NewGuid(),
            FirstName = "Robert",
            LastName = "Johnson",
            Email = "robert.johnson@company.com",
            EmployeeCode = "EMP001",
            HireDate = DateTime.SpecifyKind(new DateTime(2015, 1, 1), DateTimeKind.Utc),
            IsActive = true,
            Salary = 150000m,
            EmploymentType = EmploymentType.FullTime,
            Department = "Executive",
            Position = "CEO",
            Address = new Address
            {
                Street = "100 Corporate Plaza",
                City = "San Francisco",
                State = "CA",
                Country = "USA",
                ZipCode = "94102"
            },
            Skills =
            [
                new Skill { Name = "Leadership", ProficiencyLevel = 5, YearsOfExperience = 20 },
                new Skill { Name = "Strategic Planning", ProficiencyLevel = 5, YearsOfExperience = 18 }
            ],
            EmergencyContacts =
            [
                new EmergencyContact
                {
                    Name = "Mary Johnson",
                    Relationship = "Spouse",
                    PhoneNumber = "+1-555-0999"
                }
            ]
        };

        await context.Employees.AddAsync(ceo);

        await context.SaveChangesAsync();

        var manager = new Employee
        {
            Id = Guid.NewGuid(),
            FirstName = "Sarah",
            LastName = "Williams",
            Email = "sarah.williams@company.com",
            EmployeeCode = "EMP002",
            HireDate = DateTime.SpecifyKind(new DateTime(2018, 3, 15), DateTimeKind.Utc),
            IsActive = true,
            Salary = 95000m,
            EmploymentType = EmploymentType.FullTime,
            Department = "Engineering",
            Position = "Engineering Manager",
            ManagerId = ceo.Id,
            Address = new Address
            {
                Street = "200 Tech Street",
                City = "San Francisco",
                State = "CA",
                Country = "USA",
                ZipCode = "94103"
            },
            Skills =
            [
                new Skill { Name = "C#", ProficiencyLevel = 5, YearsOfExperience = 10 },
                new Skill { Name = "Team Management", ProficiencyLevel = 4, YearsOfExperience = 5 }
            ],
            Certifications =
            [
                new Certification
                {
                    Name = "Microsoft Certified: Azure Solutions Architect",
                    IssuingOrganization = "Microsoft",
                    IssueDate = DateTime.SpecifyKind(new DateTime(2022, 6, 1), DateTimeKind.Utc),
                    ExpiryDate = DateTime.SpecifyKind(new DateTime(2025, 6, 1), DateTimeKind.Utc)
                }
            ]
        };

        await context.Employees.AddAsync(manager);

        await context.SaveChangesAsync();

        // Seed Orders (after customers and products are saved)
        var order1 = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}",
            OrderDate = DateTime.UtcNow.AddDays(-10).ToUniversalTime(),
            Status = OrderStatus.Delivered,
            SubTotal = 999.99m,
            TaxAmount = 80.00m,
            ShippingCost = 10.00m,
            DiscountAmount = 50.00m,
            TotalAmount = 1039.99m,
            PaymentMethod = PaymentMethod.CreditCard,
            IsPaid = true,
            ShippedDate = DateTime.UtcNow.AddDays(-8).ToUniversalTime(),
            DeliveredDate = DateTime.UtcNow.AddDays(-5).ToUniversalTime(),
            CustomerId = customers[0].Id,
            ShippingAddress = new Address
            {
                Street = "123 Main Street",
                City = "New York",
                State = "NY",
                Country = "USA",
                ZipCode = "10001",
                Latitude = 40.7128,
                Longitude = -74.0060
            },
            BillingAddress = new Address
            {
                Street = "123 Main Street",
                City = "New York",
                State = "NY",
                Country = "USA",
                ZipCode = "10001",
                Latitude = 40.7128,
                Longitude = -74.0060
            },
            Notes =
            [
                new OrderNote
                {
                    Note = "Customer requested express shipping",
                    CreatedAt = DateTime.UtcNow.AddDays(-10).ToUniversalTime(),
                    CreatedBy = "System",
                    IsInternal = true
                }
            ],
            TrackingInfo = JsonDocument.Parse("""
            {
              "carrier": "FedEx",
              "trackingNumber": "123456789012",
              "estimatedDelivery": "2024-01-15"
            }
            """)
        };

        context.Orders.Add(order1);

        await context.SaveChangesAsync();

        var orderItem1 = new OrderItem
        {
            Id = Guid.NewGuid(),
            Quantity = 1,
            UnitPrice = 999.99m,
            Discount = 50.00m,
            TotalPrice = 949.99m,
            OrderId = order1.Id,
            ProductId = products[0].Id,
            CustomizationOptions = JsonDocument.Parse("""
            {
                "color": "Natural Titanium",
                "storage": "256GB",
                "appleCarePlus": true
            }
            """)
        };

        context.OrderItems.Add(orderItem1);

        await context.SaveChangesAsync();

        // Seed Reviews
        var review1 = new Review
        {
            Id = Guid.NewGuid(),
            Title = "Excellent phone!",
            Content = "The iPhone 15 Pro is amazing. The camera quality is outstanding and the battery life is great.",
            Rating = 5,
            CreatedAt = DateTime.UtcNow.AddDays(-7).ToUniversalTime(),
            IsVerifiedPurchase = true,
            HelpfulCount = 15,
            NotHelpfulCount = 1,
            CustomerId = customers[0].Id,
            ProductId = products[0].Id,
            ImageUrls =
            [
                "https://example.com/review-images/img1.jpg",
                "https://example.com/review-images/img2.jpg"
            ],
            Details = new ReviewDetails
            {
                Pros = ["Great camera", "Long battery life", "Premium build"],
                Cons = ["Expensive", "No charger in box"],
                RatingBreakdown = new Dictionary<string, int>
                {
                    { "camera", 5 },
                    { "battery", 5 },
                    { "performance", 5 },
                    { "design", 5 }
                }
            }
        };

        context.Reviews.Add(review1);

        await context.SaveChangesAsync();
    }
}
