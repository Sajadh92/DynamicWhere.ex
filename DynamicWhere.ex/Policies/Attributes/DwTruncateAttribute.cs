namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Shortens the decorated member's text to a maximum length.
/// </summary>
/// <remarks>
/// Runs last in the chain, so the length cap is the final word on what leaves the library however
/// long the preceding stages made the value. Text only: it emits text, so it is valid only where
/// text is assignable.
/// </remarks>
/// <example>
/// <code>
/// [DwTruncate(200, Ellipsis = "...")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwTruncateAttribute : DwPolicyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="length">The greatest number of characters kept, before any ellipsis.</param>
    public DwTruncateAttribute(int length) => Length = length;

    /// <summary>The greatest number of characters kept, before any ellipsis.</summary>
    public int Length { get; }

    /// <summary>Appended when the value was actually shortened, or null to append nothing.</summary>
    public string? Ellipsis { get; set; }
}
