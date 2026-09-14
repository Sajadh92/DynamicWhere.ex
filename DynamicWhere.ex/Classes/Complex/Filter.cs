using DynamicWhere.ex.Classes.Core;

namespace DynamicWhere.ex.Classes.Complex;

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
    /// Represents a list of fields to be selected.
    /// </summary>
    public List<string>? Selects { get; set; }

    /// <summary>
    /// Represents a list of order-by criteria.
    /// </summary>
    public List<OrderBy>? Orders { get; set; }

    /// <summary>
    /// Represents pagination settings.
    /// </summary>
    public PageBy? Page { get; set; }

    /// <summary>
    /// Returns a deep copy, so a guarded query can be canonicalized and rewritten without the
    /// caller's own filter changing underneath them.
    /// </summary>
    /// <remarks>
    /// A null branch stays null rather than becoming an empty collection. The gate decides whether
    /// to synthesize a projection by testing <see cref="Selects"/> for null, so the distinction
    /// carries meaning.
    /// </remarks>
    internal Filter Clone() => new()
    {
        ConditionGroup = ConditionGroup?.Clone(),
        Selects = Selects is null ? null : new List<string>(Selects),
        Orders = Orders?.ConvertAll(o => o.Clone()),
        Page = Page?.Clone()
    };
}
