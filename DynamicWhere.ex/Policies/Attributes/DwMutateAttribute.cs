namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Hands the decorated member's value to a transformer the application supplies.
/// </summary>
/// <remarks>
/// The escape hatch for everything the built-in strategies cannot express: a salary band, a
/// role-aware redaction, a lookup against another system. It runs first in the chain, so it sees the
/// real value rather than one another stage has already reshaped.
/// <para>
/// The type is resolved from the service provider when policy options carry one, and through
/// <c>Activator.CreateInstance</c> otherwise. One instance is reused for the life of the
/// process, so an implementation must be stateless and safe to call from many threads at once.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwMutate(typeof(SalaryBandTransformer))]
/// public decimal Salary { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwMutateAttribute : DwPolicyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="transformer">
    /// A type implementing <c>IValueTransformer</c>. Checked by the startup scan, because a type
    /// that does not implement it fails on a query rather than on deployment.
    /// </param>
    public DwMutateAttribute(Type transformer) => Transformer = transformer;

    /// <summary>The transformer to run.</summary>
    public Type Transformer { get; }

    /// <summary>
    /// True to permit aggregating this field despite the transform.
    /// </summary>
    /// <remarks>
    /// False by default, and that default is design section 7.2's mitigation. Aggregation runs in
    /// SQL against the stored values, before any transform applies, so <c>MAX</c> over a transformed
    /// field returns the real maximum — a group of one under any aggregate function hands back the
    /// exact value this attribute exists to obscure.
    /// <para>
    /// Opting in without a group-size floor is an opening rather than a permission. Set
    /// <see cref="MinGroupSize"/>, or the global <c>DwCaps.MinGroupSize</c>, alongside it.
    /// </para>
    /// </remarks>
    public bool AllowAggregate { get; set; }

    /// <summary>
    /// The smallest group this field may be aggregated over, overriding the global floor when it is
    /// larger. Zero leaves the global setting to decide.
    /// </summary>
    /// <remarks>
    /// The effective floor is the largest of this and every other aggregated field's, so a summary
    /// touching one especially sensitive column is held to that column's standard rather than the
    /// weakest one present.
    /// </remarks>
    public int MinGroupSize { get; set; }

}
