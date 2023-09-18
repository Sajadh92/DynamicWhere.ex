namespace DynamicWhere.ex;

/// <summary>
/// Represents a filter containing a condition group, order-by criteria, and pagination settings.
/// </summary>
public class Filter
{
    /// <summary>
    /// Represents a condition group containing a list of conditions and a logical operator.
    /// </summary>
    public ConditionGroup? ConditionGroup { get; set; }

    /// <summary>
    /// Represents a list of order-by criteria.
    /// </summary>
    public List<OrderBy>? Orders { get; set; }

    /// <summary>
    /// Represents pagination settings.
    /// </summary>
    public PageBy? Page { get; set; }
}
