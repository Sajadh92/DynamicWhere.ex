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
    /// <returns>
    /// A new filter. Every node is new — the condition tree, each list and each clause — so nothing
    /// either one is given afterwards reaches the other. The values inside a condition's
    /// <c>Values</c> list are the same objects: the list is new, and what the caller put in it is
    /// theirs, decoded from JSON and never written to.
    /// </returns>
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
    /// <para>
    /// A null entry inside a list stays a null entry, here and on <see cref="Segment.Clone"/> and
    /// <see cref="Summary.Clone"/>. A copy copies what is there: the request is malformed, and it is
    /// for the method that runs it to refuse it, which it does with a <c>LogicException</c>.
    /// </para>
    /// </remarks>
    public Filter Clone() => new()
    {
        ConditionGroup = ConditionGroup?.Clone(),
        Selects = Selects is null ? null : new List<string>(Selects),
        Orders = Orders?.ConvertAll(o => o?.Clone()!),
        Page = Page?.Clone()
    };
}
