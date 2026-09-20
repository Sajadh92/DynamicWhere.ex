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
    /// <returns>A new filter sharing no object with this one.</returns>
    /// <remarks>
    /// Public because callers need the same thing the library needs: a request read a second time
    /// with one part changed — the next page, another order — without editing the object a caller
    /// handed in. Rebuilding a filter around the caller's own clauses shares those clauses, and a
    /// later rewrite of either one then reaches both.
    /// <para>
    /// The copy reaches every branch: the condition tree with its groups and conditions, the
    /// projection list, each order, and the page.
    /// </para>
    /// <para>
    /// A null branch stays null rather than becoming an empty collection. The gate decides whether
    /// to synthesize a projection by testing <see cref="Selects"/> for null, so the distinction
    /// carries meaning.
    /// </para>
    /// </remarks>
    public Filter Clone() => new()
    {
        ConditionGroup = ConditionGroup?.Clone(),
        Selects = Selects is null ? null : new List<string>(Selects),
        Orders = Orders?.ConvertAll(o => o.Clone()),
        Page = Page?.Clone()
    };
}
