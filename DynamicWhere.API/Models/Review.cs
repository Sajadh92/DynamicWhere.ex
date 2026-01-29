using System.ComponentModel.DataAnnotations;

namespace DynamicWhere.API.Models;

/// <summary>
/// Review entity for product reviews
/// </summary>
public class Review
{
    [Key]
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Rating { get; set; } // 1-5

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsVerifiedPurchase { get; set; }

    public int HelpfulCount { get; set; }

    public int NotHelpfulCount { get; set; }

    // JSON field for images
    public List<string> ImageUrls { get; set; } = [];

    // JSON field for pros and cons
    public ReviewDetails? Details { get; set; }

    // Navigation properties
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // Self-referencing for replies
    public Guid? ParentReviewId { get; set; }
    public Review? ParentReview { get; set; }

    public ICollection<Review> Replies { get; set; } = [];
}

/// <summary>
/// Complex type for review details
/// </summary>
public class ReviewDetails
{
    public List<string> Pros { get; set; } = [];
    public List<string> Cons { get; set; } = [];
    public Dictionary<string, int>? RatingBreakdown { get; set; }
}
