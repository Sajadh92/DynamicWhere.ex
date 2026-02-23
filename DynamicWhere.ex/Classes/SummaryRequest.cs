namespace DynamicWhere.ex.Classes;

/// <summary>
/// Represents a summary request containing a condition group, group-by criteria, order-by criteria, and pagination settings.
/// </summary>
public class SummaryRequest
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
    /// Represents a list of order-by criteria. Each order field must exist in the group-by fields or aggregate-by aliases.
    /// </summary>
    public List<OrderBy>? Orders { get; set; }

    /// <summary>
    /// Represents pagination settings.
    /// </summary>
    public PageBy? Page { get; set; }
}
