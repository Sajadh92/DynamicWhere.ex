namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// Names the query features a policy can speak to. Combined as flags so one fragment can
/// address several features at once, for example <c>Select | Order</c>.
/// </summary>
[Flags]
public enum PolicyFeature
{
    /// <summary>No feature. Used as an accumulator seed, never stored on a fragment.</summary>
    None = 0,

    /// <summary>Filtering, through <c>Condition</c> and <c>ConditionGroup</c>.</summary>
    Where = 1,

    /// <summary>Projection, through <c>Filter.Selects</c>.</summary>
    Select = 2,

    /// <summary>Sorting, through <c>Filter.Orders</c>.</summary>
    Order = 4,

    /// <summary>Grouping, through <c>Summary.GroupBy</c>.</summary>
    Group = 8,

    /// <summary>Aggregation, through <c>Summary.AggregateBy</c>.</summary>
    Aggregate = 16,

    /// <summary>
    /// Participation in a set operation. Held separately from <see cref="Where"/> and
    /// <see cref="Select"/> because Union, Intersect, and Except form their own disclosure
    /// channel: set membership can reconstruct a field that was never projected.
    /// </summary>
    Segment = 32,

    /// <summary>Every feature.</summary>
    All = Where | Select | Order | Group | Aggregate | Segment
}
