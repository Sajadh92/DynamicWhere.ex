namespace DynamicWhere.Tests.Domain;

/// <summary>
/// Sales-domain model mirroring the entities the API test controllers query, trimmed to the
/// members those controllers actually exercise. Self-contained so the test suite does not depend
/// on the API project or on PostgreSQL.
/// </summary>

public enum OrderStatus { Pending, Confirmed, Processing, Shipped, Delivered, Cancelled, Refunded }

public enum PaymentMethod { CreditCard, DebitCard, PayPal, BankTransfer, CashOnDelivery }

public enum CustomerTier { Bronze, Silver, Gold, Platinum }

public enum Gender { Male, Female, Other }

/// <summary>Owned complex type used for <c>ShippingAddress.Country</c> style nested paths.</summary>
public class Address
{
    public string? Street { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? Country { get; set; }

    public string? ZipCode { get; set; }
}

/// <summary>Owned complex type hanging off <see cref="Customer"/>.</summary>
public class ContactInfo
{
    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }
}

/// <summary>Self-referencing hierarchy, also the reference navigation target for <see cref="Product"/>.</summary>
public class Category
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    public Guid? ParentCategoryId { get; set; }

    public Category? ParentCategory { get; set; }

    public ICollection<Category> SubCategories { get; set; } = new List<Category>();

    public ICollection<Product> Products { get; set; } = new List<Product>();
}

/// <summary>Carries one property of every DataType the library supports, plus nullable variants.</summary>
public class Product
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public double Rating { get; set; }

    public int StockQuantity { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateOnly? ManufactureDate { get; set; }

    public TimeOnly? AvailableFrom { get; set; }

    /// <summary>Collection of simple values — projected whole and used as an order key.</summary>
    public List<string> Tags { get; set; } = new();

    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    public ICollection<Review> Reviews { get; set; } = new List<Review>();

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}

public class Customer
{
    public Guid Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public Gender Gender { get; set; }

    public CustomerTier Tier { get; set; }

    public decimal TotalSpent { get; set; }

    public bool IsActive { get; set; }

    public DateTime RegisteredAt { get; set; }

    public ContactInfo ContactInfo { get; set; } = new();

    public ICollection<Order> Orders { get; set; } = new List<Order>();

    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}

public class Order
{
    public Guid Id { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public DateTime OrderDate { get; set; }

    public OrderStatus Status { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public decimal TotalAmount { get; set; }

    public bool IsPaid { get; set; }

    public Address ShippingAddress { get; set; } = new();

    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}

public class OrderItem
{
    public Guid Id { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Discount { get; set; }

    public decimal TotalPrice { get; set; }

    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    public Guid ProductId { get; set; }

    public Product? Product { get; set; }
}

public class Review
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Rating { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsVerifiedPurchase { get; set; }

    public int HelpfulCount { get; set; }

    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public Guid ProductId { get; set; }

    public Product? Product { get; set; }
}
