using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace DynamicWhere.API.Models;

/// <summary>
/// Customer entity with complex nested types
/// </summary>
public class Customer
{
    [Key]
    public Guid Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public DateTime DateOfBirth { get; set; }

    public Gender Gender { get; set; }

    public bool IsActive { get; set; }

    public DateTime RegisteredAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    // Complex type (owned entity)
    public ContactInfo ContactInfo { get; set; } = new();

    // JSON field for addresses (nested list)
    public List<Address> Addresses { get; set; } = [];

    // JSON field for preferences
    public JsonDocument? Preferences { get; set; }

    // JSON field for loyalty points history
    public List<LoyaltyPoint> LoyaltyPoints { get; set; } = [];

    public decimal TotalSpent { get; set; }

    public CustomerTier Tier { get; set; }

    // Navigation properties
    public ICollection<Order> Orders { get; set; } = [];

    public ICollection<Review> Reviews { get; set; } = [];
}

public enum Gender
{
    NotSpecified = 0,
    Male = 1,
    Female = 2,
    Other = 3
}

public enum CustomerTier
{
    Bronze = 0,
    Silver = 1,
    Gold = 2,
    Platinum = 3
}

/// <summary>
/// Complex type for loyalty points
/// </summary>
public class LoyaltyPoint
{
    public int Points { get; set; }
    public DateTime EarnedDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsRedeemed { get; set; }
}
