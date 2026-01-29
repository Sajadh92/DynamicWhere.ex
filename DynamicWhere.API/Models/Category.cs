using System.ComponentModel.DataAnnotations;

namespace DynamicWhere.API.Models;

/// <summary>
/// Category entity with hierarchical structure
/// </summary>
public class Category
{
    [Key]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    // Self-referencing for nested categories
    public Guid? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }

    public ICollection<Category> SubCategories { get; set; } = [];

    // JSON field for metadata
    public Dictionary<string, string>? Metadata { get; set; }

    // Navigation property
    public ICollection<Product> Products { get; set; } = [];
}
