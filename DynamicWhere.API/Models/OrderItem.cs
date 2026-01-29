using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace DynamicWhere.API.Models;

/// <summary>
/// Order item entity (line items in an order)
/// </summary>
public class OrderItem
{
    [Key]
    public Guid Id { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Discount { get; set; }

    public decimal TotalPrice { get; set; }

    // JSON field for customization options
    public JsonDocument? CustomizationOptions { get; set; }

    // Navigation properties
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
}
