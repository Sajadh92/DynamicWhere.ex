using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Classes.Core;

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

    /// <summary>
    /// Returns a deep copy, recursing through <see cref="SubConditionGroups"/>.
    /// </summary>
    /// <remarks>
    /// The recursion is the point. A shallow copy of a group would share the nested groups with
    /// the original, and the validator rewrites condition fields at every depth.
    /// </remarks>
    internal ConditionGroup Clone() => new()
    {
        Sort = Sort,
        Connector = Connector,
        Conditions = Conditions is null ? null! : Conditions.ConvertAll(c => c.Clone()),
        SubConditionGroups = SubConditionGroups is null
            ? null!
            : SubConditionGroups.ConvertAll(g => g.Clone())
    };
}
