using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace DynamicWhere.API.Models;

/// <summary>
/// Order entity with nested order items and complex fields
/// </summary>
public class Order
{
    [Key]
    public Guid Id { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public DateTime OrderDate { get; set; }

    public DateTime? ShippedDate { get; set; }

    public DateTime? DeliveredDate { get; set; }

    public OrderStatus Status { get; set; }

    public decimal SubTotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal ShippingCost { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public bool IsPaid { get; set; }

    // Complex type for shipping address
    public Address ShippingAddress { get; set; } = new();

    // Complex type for billing address
    public Address BillingAddress { get; set; } = new();

    // JSON field for tracking info
    public JsonDocument? TrackingInfo { get; set; }

    // JSON field for notes/comments
    public List<OrderNote> Notes { get; set; } = [];

    // Navigation properties
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public ICollection<OrderItem> OrderItems { get; set; } = [];
}

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Processing = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 5,
    Refunded = 6
}

public enum PaymentMethod
{
    CreditCard = 0,
    DebitCard = 1,
    PayPal = 2,
    BankTransfer = 3,
    CashOnDelivery = 4,
    Cryptocurrency = 5
}

/// <summary>
/// Complex type for order notes
/// </summary>
public class OrderNote
{
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
}
