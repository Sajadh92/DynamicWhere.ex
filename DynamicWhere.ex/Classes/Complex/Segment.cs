using DynamicWhere.ex.Classes.Core;

namespace DynamicWhere.ex.Classes.Complex;

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

    /// <summary>
    /// Returns a deep copy, cloning every condition set independently.
    /// </summary>
    /// <returns>
    /// A new segment. Every node is new — the condition tree, each list and each clause — so nothing
    /// either one is given afterwards reaches the other. The values inside a condition's
    /// <c>Values</c> list are the same objects: the list is new, and what the caller put in it is
    /// theirs, decoded from JSON and never written to.
    /// </returns>
    /// <remarks>
    /// Policy applies to each set on its own, so the sets must not share a condition group: a
    /// rewrite aimed at one would otherwise land on all of them.
    /// <para>
    /// Public for the same reason <see cref="Filter.Clone"/> is: read a caller's request again with
    /// one part changed, without editing what the caller handed in.
    /// </para>
    /// </remarks>
    public Segment Clone() => new()
    {
        ConditionSets = ConditionSets is null ? null! : ConditionSets.ConvertAll(s => s?.Clone()!),
        Selects = Selects is null ? null : new List<string>(Selects),
        Orders = Orders?.ConvertAll(o => o?.Clone()!),
        Page = Page?.Clone()
    };
}
