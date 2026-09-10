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
    /// <remarks>
    /// A summary reaches a condition group twice — once through <see cref="ConditionGroup"/> and
    /// again through <see cref="Having"/>. Both are cloned; a copy shaped like a filter's would
    /// leave the having clause shared with the caller.
    /// </remarks>
    internal Summary Clone() => new()
    {
        ConditionGroup = ConditionGroup?.Clone(),
        GroupBy = GroupBy?.Clone(),
        Having = Having?.Clone(),
        Orders = Orders?.ConvertAll(o => o.Clone()),
        Page = Page?.Clone()
    };
}
