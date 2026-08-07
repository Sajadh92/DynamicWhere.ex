namespace DynamicWhere.Tests.Domain;

/// <summary>
/// Deterministic seed data. Every value is fixed — no <c>Guid.NewGuid</c>, no <c>DateTime.Now</c> —
/// so each assertion can name an exact expected result.
/// </summary>
/// <remarks>
/// The product names and numbers are chosen so that every operator under test returns a distinct,
/// non-empty, non-total count. Deliberate gaps: product 7 has no category, products 2/4/8 have no
/// description, products 2/4/6 have no <c>UpdatedAt</c>, product 6 has no tags, product 4 has no
/// order items, and order 5 has no order items — these cover null and empty-collection paths.
/// </remarks>
internal static class SalesSeed
{
    /// <summary>Builds a stable Guid from an integer so ids are readable and repeatable.</summary>
    public static Guid Id(int seed) => new(seed, 0, 0, new byte[8]);

    // Categories
    public static Guid Electronics => Id(101);
    public static Guid Smartphones => Id(102);
    public static Guid Laptops => Id(103);
    public static Guid Clearance => Id(104);

    // Products
    public static Guid ProductPro => Id(201);
    public static Guid ProductProLower => Id(202);
    public static Guid ProductProBook => Id(203);
    public static Guid ProductLaptopPro => Id(204);
    public static Guid ProductUltra => Id(205);
    public static Guid ProductBasic => Id(206);
    public static Guid ProductWidget => Id(207);
    public static Guid ProductGadgetPro => Id(208);

    // Customers
    public static Guid CustomerJohn => Id(301);
    public static Guid CustomerJane => Id(302);
    public static Guid CustomerJack => Id(303);
    public static Guid CustomerAlice => Id(304);

    // Orders
    public static Guid Order1001 => Id(401);
    public static Guid Order1002 => Id(402);
    public static Guid Order1003 => Id(403);
    public static Guid Order1004 => Id(404);
    public static Guid Order1005 => Id(405);
    public static Guid Order1006 => Id(406);

    public static List<Category> Categories() =>
    [
        new() { Id = Electronics, Name = "Electronics", Description = "Devices and accessories", DisplayOrder = 1, IsActive = true },
        new() { Id = Smartphones, Name = "Smartphones", Description = "Mobile phones", DisplayOrder = 2, IsActive = true, ParentCategoryId = Electronics },
        new() { Id = Laptops, Name = "Laptops", Description = null, DisplayOrder = 3, IsActive = true, ParentCategoryId = Electronics },
        new() { Id = Clearance, Name = "Clearance", Description = "Discontinued stock", DisplayOrder = 4, IsActive = false }
    ];

    public static List<Product> Products() =>
    [
        new()
        {
            Id = ProductPro, Name = "Pro", Description = "Flagship handset",
            Price = 150.00m, Rating = 4.5, StockQuantity = 10, IsActive = true,
            CreatedAt = new DateTime(2024, 3, 1), UpdatedAt = new DateTime(2024, 6, 1),
            ManufactureDate = new DateOnly(2024, 2, 1), AvailableFrom = new TimeOnly(9, 0),
            Tags = ["wireless", "flagship"], CategoryId = Smartphones
        },
        new()
        {
            Id = ProductProLower, Name = "pro", Description = null,
            Price = 45.50m, Rating = 3.5, StockQuantity = 0, IsActive = true,
            CreatedAt = new DateTime(2023, 5, 10), UpdatedAt = null,
            ManufactureDate = null, AvailableFrom = null,
            Tags = ["budget"], CategoryId = Smartphones
        },
        new()
        {
            Id = ProductProBook, Name = "ProBook", Description = "Business laptop",
            Price = 999.99m, Rating = 4.8, StockQuantity = 5, IsActive = true,
            CreatedAt = new DateTime(2024, 1, 15), UpdatedAt = new DateTime(2024, 7, 20),
            ManufactureDate = new DateOnly(2023, 12, 20), AvailableFrom = new TimeOnly(8, 30),
            Tags = ["laptop", "business", "premium"], CategoryId = Laptops
        },
        new()
        {
            Id = ProductLaptopPro, Name = "Laptop Pro", Description = null,
            Price = 250.00m, Rating = 2.5, StockQuantity = 50, IsActive = false,
            CreatedAt = new DateTime(2022, 11, 5), UpdatedAt = null,
            ManufactureDate = null, AvailableFrom = null,
            Tags = ["laptop"], CategoryId = Laptops
        },
        new()
        {
            Id = ProductUltra, Name = "Ultra", Description = "Mid tier device",
            Price = 75.25m, Rating = 4.0, StockQuantity = 100, IsActive = true,
            CreatedAt = new DateTime(2024, 2, 20), UpdatedAt = new DateTime(2024, 5, 15),
            ManufactureDate = new DateOnly(2024, 1, 5), AvailableFrom = new TimeOnly(10, 15),
            Tags = ["mid", "popular"], CategoryId = Electronics
        },
        new()
        {
            Id = ProductBasic, Name = "Basic", Description = "Entry level",
            Price = 19.99m, Rating = 1.5, StockQuantity = 250, IsActive = false,
            CreatedAt = new DateTime(2023, 8, 30), UpdatedAt = null,
            ManufactureDate = null, AvailableFrom = null,
            Tags = [], CategoryId = Clearance
        },
        new()
        {
            Id = ProductWidget, Name = "Widget", Description = "Accessory",
            Price = 5.00m, Rating = 3.5, StockQuantity = 1, IsActive = true,
            CreatedAt = new DateTime(2025, 1, 10), UpdatedAt = new DateTime(2025, 2, 1),
            ManufactureDate = new DateOnly(2024, 12, 1), AvailableFrom = new TimeOnly(7, 45),
            Tags = ["accessory"], CategoryId = null
        },
        new()
        {
            Id = ProductGadgetPro, Name = "Gadget pro", Description = null,
            Price = 550.75m, Rating = 5.0, StockQuantity = 0, IsActive = true,
            CreatedAt = new DateTime(2023, 2, 14), UpdatedAt = new DateTime(2024, 1, 1),
            ManufactureDate = null, AvailableFrom = new TimeOnly(12, 0),
            Tags = ["gadget", "premium"], CategoryId = Electronics
        }
    ];

    public static List<Customer> Customers() =>
    [
        new()
        {
            Id = CustomerJohn, FirstName = "John", LastName = "Smith", Username = "jsmith",
            Gender = Gender.Male, Tier = CustomerTier.Gold, TotalSpent = 1500.00m, IsActive = true,
            RegisteredAt = new DateTime(2023, 1, 15),
            ContactInfo = new ContactInfo { Email = "john@example.com", PhoneNumber = "555-0101" }
        },
        new()
        {
            Id = CustomerJane, FirstName = "Jane", LastName = "Doe", Username = "jdoe",
            Gender = Gender.Female, Tier = CustomerTier.Silver, TotalSpent = 750.50m, IsActive = true,
            RegisteredAt = new DateTime(2023, 6, 20),
            ContactInfo = new ContactInfo { Email = "jane@example.com", PhoneNumber = "555-0102" }
        },
        new()
        {
            Id = CustomerJack, FirstName = "Jack", LastName = "Brown", Username = "jbrown",
            Gender = Gender.Male, Tier = CustomerTier.Gold, TotalSpent = 2500.00m, IsActive = false,
            RegisteredAt = new DateTime(2022, 3, 10),
            ContactInfo = new ContactInfo { Email = "jack@example.com", PhoneNumber = null }
        },
        new()
        {
            Id = CustomerAlice, FirstName = "Alice", LastName = "White", Username = "awhite",
            Gender = Gender.Female, Tier = CustomerTier.Bronze, TotalSpent = 120.00m, IsActive = true,
            RegisteredAt = new DateTime(2024, 2, 1),
            ContactInfo = new ContactInfo { Email = "alice@example.com", PhoneNumber = "555-0104" }
        }
    ];

    public static List<Order> Orders() =>
    [
        new()
        {
            Id = Order1001, OrderNumber = "ORD-1001", OrderDate = new DateTime(2023, 3, 15),
            Status = OrderStatus.Pending, PaymentMethod = PaymentMethod.CreditCard,
            TotalAmount = 250.00m, IsPaid = true, CustomerId = CustomerJohn,
            ShippingAddress = new Address { Street = "1 Main St", City = "Austin", State = "TX", Country = "USA", ZipCode = "73301" }
        },
        new()
        {
            Id = Order1002, OrderNumber = "ORD-1002", OrderDate = new DateTime(2023, 8, 20),
            Status = OrderStatus.Processing, PaymentMethod = PaymentMethod.PayPal,
            TotalAmount = 500.00m, IsPaid = true, CustomerId = CustomerJohn,
            ShippingAddress = new Address { Street = "1 Main St", City = "Austin", State = "TX", Country = "USA", ZipCode = "73301" }
        },
        new()
        {
            Id = Order1003, OrderNumber = "ORD-1003", OrderDate = new DateTime(2024, 1, 10),
            Status = OrderStatus.Shipped, PaymentMethod = PaymentMethod.CreditCard,
            TotalAmount = 125.75m, IsPaid = false, CustomerId = CustomerJane,
            ShippingAddress = new Address { Street = "9 King St", City = "Toronto", State = "ON", Country = "Canada", ZipCode = "M5H" }
        },
        new()
        {
            Id = Order1004, OrderNumber = "ORD-1004", OrderDate = new DateTime(2024, 5, 5),
            Status = OrderStatus.Delivered, PaymentMethod = PaymentMethod.BankTransfer,
            TotalAmount = 900.00m, IsPaid = true, CustomerId = CustomerJack,
            ShippingAddress = new Address { Street = "5 Hauptstr", City = "Berlin", State = "BE", Country = "Germany", ZipCode = "10115" }
        },
        new()
        {
            Id = Order1005, OrderNumber = "ORD-1005", OrderDate = new DateTime(2022, 12, 1),
            Status = OrderStatus.Pending, PaymentMethod = PaymentMethod.CreditCard,
            TotalAmount = 50.00m, IsPaid = false, CustomerId = CustomerAlice,
            ShippingAddress = new Address { Street = "3 Oak Ave", City = "Dallas", State = "TX", Country = "USA", ZipCode = "75201" }
        },
        new()
        {
            Id = Order1006, OrderNumber = "ORD-1006", OrderDate = new DateTime(2024, 7, 4),
            Status = OrderStatus.Cancelled, PaymentMethod = PaymentMethod.PayPal,
            TotalAmount = 75.25m, IsPaid = false, CustomerId = CustomerJane,
            ShippingAddress = new Address { Street = "9 King St", City = "Toronto", State = "ON", Country = "Canada", ZipCode = "M5H" }
        }
    ];

    /// <summary>Order 1005 deliberately has no items; product "Laptop Pro" is never ordered.</summary>
    public static List<OrderItem> OrderItems() =>
    [
        new() { Id = Id(501), OrderId = Order1001, ProductId = ProductPro, Quantity = 2, UnitPrice = 150.00m, Discount = 0m, TotalPrice = 300.00m },
        new() { Id = Id(502), OrderId = Order1001, ProductId = ProductUltra, Quantity = 1, UnitPrice = 75.25m, Discount = 5.00m, TotalPrice = 70.25m },
        new() { Id = Id(503), OrderId = Order1002, ProductId = ProductProBook, Quantity = 1, UnitPrice = 999.99m, Discount = 0m, TotalPrice = 999.99m },
        new() { Id = Id(504), OrderId = Order1003, ProductId = ProductWidget, Quantity = 5, UnitPrice = 5.00m, Discount = 0m, TotalPrice = 25.00m },
        new() { Id = Id(505), OrderId = Order1004, ProductId = ProductGadgetPro, Quantity = 1, UnitPrice = 550.75m, Discount = 50.00m, TotalPrice = 500.75m },
        new() { Id = Id(506), OrderId = Order1004, ProductId = ProductProLower, Quantity = 3, UnitPrice = 45.50m, Discount = 0m, TotalPrice = 136.50m },
        new() { Id = Id(507), OrderId = Order1006, ProductId = ProductBasic, Quantity = 2, UnitPrice = 19.99m, Discount = 0m, TotalPrice = 39.98m }
    ];

    /// <summary>Products 2, 4, 7 and 8 deliberately have no reviews.</summary>
    public static List<Review> Reviews() =>
    [
        new() { Id = Id(601), ProductId = ProductPro, CustomerId = CustomerJohn, Title = "Great phone", Content = "Fast and sharp", Rating = 5, CreatedAt = new DateTime(2024, 4, 1), UpdatedAt = new DateTime(2024, 4, 10), IsVerifiedPurchase = true, HelpfulCount = 12 },
        new() { Id = Id(602), ProductId = ProductPro, CustomerId = CustomerJane, Title = "Good but pricey", Content = "Works well", Rating = 4, CreatedAt = new DateTime(2024, 5, 1), UpdatedAt = null, IsVerifiedPurchase = false, HelpfulCount = 3 },
        new() { Id = Id(603), ProductId = ProductProBook, CustomerId = CustomerJohn, Title = "Solid laptop", Content = "Great keyboard", Rating = 5, CreatedAt = new DateTime(2024, 2, 1), UpdatedAt = null, IsVerifiedPurchase = true, HelpfulCount = 8 },
        new() { Id = Id(604), ProductId = ProductBasic, CustomerId = CustomerAlice, Title = "Cheap and cheerful", Content = "Does the job", Rating = 2, CreatedAt = new DateTime(2023, 9, 15), UpdatedAt = new DateTime(2023, 10, 1), IsVerifiedPurchase = true, HelpfulCount = 1 },
        new() { Id = Id(605), ProductId = ProductUltra, CustomerId = CustomerJack, Title = "Average", Content = "Nothing special", Rating = 3, CreatedAt = new DateTime(2024, 3, 20), UpdatedAt = null, IsVerifiedPurchase = false, HelpfulCount = 0 }
    ];
}
