namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Charges the decorated member against the query budget, so that a field which is expensive to
/// filter or sort on costs more of a caller's allowance than one that is not.
/// </summary>
/// <remarks>
/// This is a denial-of-service control rather than an access control, and it closes a hole that
/// predates the policy layer: before this release a filter could name a field any number of times
/// and traverse a navigation path of any depth, so one request could generate an unbounded join.
/// <c>DwCaps.MaxQueryCost</c> is the budget; every reference a query makes to a field spends this
/// weight, and the standing default applies to fields nobody weighed.
/// <para>
/// Sealed by default, like every other policy attribute. A runtime rule can raise the weight of a
/// field the source code never weighed, and can lower one only where the author wrote
/// <see cref="DwPolicyAttribute.Overridable"/> — a rule that could quietly cheapen a field would
/// turn the cap into something the store controls.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwCost(10)]
/// public string FullTextBody { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwCostAttribute : DwPolicyAttribute
{
    /// <summary>
    /// Initializes the attribute.
    /// </summary>
    /// <param name="weight">
    /// What one reference to this field spends. Zero is permitted and means the budget does not
    /// charge for the field at all; a negative weight is refused at fragment construction, because
    /// a query would otherwise buy budget back by naming the field.
    /// </param>
    public DwCostAttribute(int weight) => Weight = weight;

    /// <summary>What one reference to this field spends against the budget.</summary>
    public int Weight { get; }
}
