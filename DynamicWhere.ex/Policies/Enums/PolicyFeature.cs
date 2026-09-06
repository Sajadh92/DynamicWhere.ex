namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// Names the query features a policy can speak to. Combined as flags so one fragment can
/// address several features at once, for example <c>Select | Order</c>.
/// </summary>
[Flags]
public enum PolicyFeature
{
    /// <summary>
    /// No feature. What a fragment speaks to when it carries something without deciding anything —
    /// an alias, an operator restriction, a forced predicate, a filtering requirement, or a field's
    /// description, cost and audit flag.
    /// </summary>
    /// <remarks>
    /// Such a fragment covers no feature and so wins no election, which is the point. Every one of
    /// those carriers rides on a typed property the resolver elects, intersects or accumulates
    /// outside the per-feature contest, and an attribute is sealed unless its author says otherwise
    /// — so a carrier claiming a feature with <see cref="PolicyEffect.Allow"/> would outrank every
    /// runtime denial of it, and decorating a field would quietly make that field undeniable.
    /// <para>
    /// A <em>rule</em> may only speak to this when it carries such a thing: a rule stating neither
    /// an effect over features nor a carrier is inert, and an inert rule is a control an operator
    /// believes is in force.
    /// </para>
    /// </remarks>
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
