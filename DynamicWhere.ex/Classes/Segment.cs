namespace DynamicWhere.ex;

/// <summary>
/// Represents a segment containing multiple sets of conditions for dynamic queries.
/// </summary>
public class Segment
{
    /// <summary>
    /// The list of condition sets in the segment.
    /// </summary>
    public List<ConditionSet> ConditionSets { get; set; } = new();

    /// <summary>
    /// Represents the pagination settings for the segment.
    /// </summary>
    public PageBy? Page { get; set; }
}
