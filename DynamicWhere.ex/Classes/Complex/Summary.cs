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
}
