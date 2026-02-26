namespace DynamicWhere.ex.Classes.Core;

/// <summary>
/// Represents a group-by configuration specifying grouping fields and optional aggregations.
/// </summary>
public class GroupBy
{
    /// <summary>
    /// The list of field names to group by.
    /// </summary>
    public List<string> Fields { get; set; } = new();

    /// <summary>
    /// The list of aggregations to apply to each group.
    /// </summary>
    public List<AggregateBy> AggregateBy { get; set; } = new();
}
