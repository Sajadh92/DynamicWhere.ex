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

    /// <summary>
    /// Returns a deep copy. Both lists are rewritten in place by the validator.
    /// </summary>
    internal GroupBy Clone() => new()
    {
        Fields = Fields is null ? null! : new List<string>(Fields),
        AggregateBy = AggregateBy is null ? null! : AggregateBy.ConvertAll(a => a.Clone())
    };
}
