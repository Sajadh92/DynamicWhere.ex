using DynamicWhere.ex.Classes.Core;

namespace DynamicWhere.ex.Classes.Complex;

/// <summary>
/// Represents a summary request containing a condition group, group-by criteria, order-by criteria, and pagination settings.
/// </summary>
public class Summary
{ 
    /// <summary>
    /// Represents a condition group containing a list of conditions and a logical operator.
    /// </summary>
    public ConditionGroup? ConditionGroup { get; set; }

    /// <summary>
    /// Represents the group-by configuration specifying grouping fields and optional aggregations.
    /// </summary>
    public GroupBy? GroupBy { get; set; }

    /// <summary>
    /// Represents a condition group for the having clause, which applies conditions to the grouped results. 
    /// Each condition must reference an aggregate-by alias.
    /// </summary>
    public ConditionGroup? Having { get; set; }

    /// <summary>
    /// Represents a list of order-by criteria. Each order field must exist in the group-by fields or aggregate-by aliases.
    /// </summary>
    public List<OrderBy>? Orders { get; set; }

    /// <summary>
    /// Represents pagination settings.
    /// </summary>
    public PageBy? Page { get; set; }

    /// <summary>
    /// Returns a deep copy.
    /// </summary>
    /// <returns>
    /// A new summary. Every node is new — the condition tree, each list and each clause — so nothing
    /// either one is given afterwards reaches the other. The values inside a condition's
    /// <c>Values</c> list are the same objects: the list is new, and what the caller put in it is
    /// theirs, decoded from JSON and never written to.
    /// </returns>
    /// <remarks>
    /// A summary reaches a condition group twice — once through <see cref="ConditionGroup"/> and
    /// again through <see cref="Having"/>. Both are cloned; a copy shaped like a filter's would
    /// leave the having clause shared with the caller.
    /// <para>
    /// Public for the same reason <see cref="Filter.Clone"/> is: read a caller's request again with
    /// one part changed, without editing what the caller handed in.
    /// </para>
    /// </remarks>
    public Summary Clone() => new()
    {
        ConditionGroup = ConditionGroup?.Clone(),
        GroupBy = GroupBy?.Clone(),
        Having = Having?.Clone(),
        Orders = Orders?.ConvertAll(o => o?.Clone()!),
        Page = Page?.Clone()
    };
}
