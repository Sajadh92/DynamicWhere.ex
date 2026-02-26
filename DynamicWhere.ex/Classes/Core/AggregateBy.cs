using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Classes.Core;

/// <summary>
/// Represents an aggregation configuration specifying the field, alias, and aggregation operation.
/// </summary>
public class AggregateBy
{
    /// <summary>
    /// The field name to aggregate.
    /// </summary>
    public string? Field { get; set; }

    /// <summary>
    /// The alias for the aggregated result.
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>
    /// The aggregation operation to apply.
    /// </summary>
    public Aggregator Aggregator { get; set; }
}
