namespace DynamicWhere.ex;

/// <summary>
/// Represents a set of conditions used in dynamic queries.
/// </summary>
public class ConditionSet
{
    /// <summary>
    /// The sort order of the condition set.
    /// </summary>
    public int Sort { get; set; }

    /// <summary>
    /// The intersection type for combining conditions within the set (optional for fisrt set only).
    /// </summary>
    public Intersection? Intersection { get; set; }

    /// <summary>
    /// The condition group associated with the set.
    /// </summary>
    public ConditionGroup ConditionGroup { get; set; } = new();
}
