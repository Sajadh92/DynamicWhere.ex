namespace DynamicWhere.API.Models;

/// <summary>
/// Complex type for contact information
/// </summary>
public class ContactInfo
{
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? AlternateEmail { get; set; }
    public string? AlternatePhone { get; set; }
    public List<string> SocialMediaLinks { get; set; } = [];
}
