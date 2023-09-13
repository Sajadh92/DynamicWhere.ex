namespace DynamicWhere.ex;

/// <summary>
/// Represents a group of conditions used in dynamic queries.
/// </summary>
public class ConditionGroup
{
    /// <summary>
    /// The sort order of the condition group.
    /// </summary>
    public int Sort { get; set; }

    /// <summary>
    /// The connector used to combine conditions within the group.
    /// </summary>
    public Connector Connector { get; set; }

    /// <summary>
    /// The list of conditions within the group.
    /// </summary>
    public List<Condition> Conditions { get; set; } = new();

    /// <summary>
    /// The list of subcondition groups within the group.
    /// </summary>
    public List<ConditionGroup> SubConditionGroups { get; set; } = new();
}
