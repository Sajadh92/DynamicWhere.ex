namespace DynamicWhere.ex.Classes.Core;

/// <summary>
/// Represents a pagination configuration specifying the page number and page size.
/// </summary>
public class PageBy
{
    /// <summary>
    /// Represents the page number.
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Represents the number of items per page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Returns a copy. Page has no reference members, so this is a plain field copy.
    /// </summary>
    internal PageBy Clone() => new()
    {
        PageNumber = PageNumber,
        PageSize = PageSize
    };
}
