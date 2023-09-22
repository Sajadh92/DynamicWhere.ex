namespace DynamicWhere.ex;

/// <summary>
/// Represents the result of a segmented query execution, including pagination information and a list of entities of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The entity type of the query result.</typeparam>
public class SegmentResult<T>
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
    /// Represents the total count of entities matching the query conditions.
    /// </summary>
    public int TotalCount { get; set; } = 0;

    /// <summary>
    /// Represents the list of entities retrieved as a result of the query.
    /// </summary>
    public List<T> Data { get; set; } = new();
}
