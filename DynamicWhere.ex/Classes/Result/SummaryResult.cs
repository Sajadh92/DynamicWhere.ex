namespace DynamicWhere.ex.Classes.Result;

/// <summary>
/// Represents the result of a summary query execution, including pagination information and a list of dynamic grouped entities.
/// </summary>
public class SummaryResult
{
    /// <summary>
    /// Represents the page number of the current result page.
    /// </summary>
    public int PageNumber { get; set; } = 0;

    /// <summary>
    /// Represents the number of entities displayed on each page of results.
    /// </summary>
    public int PageSize { get; set; } = 0;

    /// <summary>
    /// Represents the total number of pages based on the specified page size and total entity count.
    /// </summary>
    public int PageCount { get; set; } = 0;

    /// <summary>
    /// Represents the total count of grouped entities matching the query conditions.
    /// </summary>
    public int TotalCount { get; set; } = 0;

    /// <summary>
    /// Represents the list of dynamic grouped entities retrieved as a result of the query.
    /// </summary>
    public List<dynamic> Data { get; set; } = new();

    /// <summary>
    /// Represents the query string that applied on database side.
    /// </summary>
    public string? QueryString { get; set; }
}
