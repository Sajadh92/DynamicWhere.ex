using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace DynamicWhere.API.Models;

/// <summary>
/// Product entity with various data types and JSON fields
/// </summary>
public class Product
{
    [Key]
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

    // JSON field for specifications
    public JsonDocument? Specifications { get; set; }

    // JSON field for tags
    public List<string> Tags { get; set; } = [];

    // JSON field for dimensions
    public Dictionary<string, double>? Dimensions { get; set; }

    // Navigation property
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    // Collection navigation
    public ICollection<OrderItem> OrderItems { get; set; } = [];

    public ICollection<Review> Reviews { get; set; } = [];
}
