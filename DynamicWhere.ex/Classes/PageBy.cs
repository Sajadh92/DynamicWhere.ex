namespace DynamicWhere.ex;

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
}
