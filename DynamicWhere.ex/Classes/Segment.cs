﻿namespace DynamicWhere.ex;

/// <summary>
/// Represents a segment containing multiple sets of conditions for dynamic queries.
/// </summary>
public class Segment
{
    /// <summary>
    /// Represents a list of condition sets in the segment.
    /// </summary>
    public List<ConditionSet> ConditionSets { get; set; } = new();

    /// <summary>
    /// Represents a list of fields to be selected.
    /// </summary>
    public List<string>? Selects { get; set; }

    /// <summary>
    /// Represents the order by for the segment.
    /// </summary>
    public List<OrderBy>? Orders { get; set; } = new();

    /// <summary>
    /// Represents the pagination for the segment.
    /// </summary>
    public PageBy? Page { get; set; }
}
