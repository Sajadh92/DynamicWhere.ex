using DTOs = DynamicWhere.ex.Policies.DTOs;

namespace DynamicWhere.ex.Classes.Result;

/// <summary>
/// Represents the result of a filtered query execution, including pagination information and a list of entities of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The entity type of the query result.</typeparam>
public class FilterResult<T>
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

    /// <summary>
    /// Represents the query string that applied on database side.
    /// </summary>
    public string? QueryString { get; set; }

    /// <summary>
    /// What the policy did to this query, or null when the query was not guarded.
    /// </summary>
    /// <remarks>
    /// A dropped field leaves nothing behind in the data, so this is the only way a caller can tell
    /// a policy drop apart from a null value.
    /// </remarks>
    public DTOs.PolicyTrace? Policy { get; set; }
}
