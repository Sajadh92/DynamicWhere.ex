namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Renders the decorated member's value through a format string.
/// </summary>
/// <remarks>
/// Runs after generalization and before masking, so a date reduced to its year is rendered and then
/// obscured rather than the other way round. Formatting emits text, so it is valid only where text
/// is assignable — a <c>DateTime</c> member cannot carry it, and a <c>string</c> member holding a
/// rendered date can.
/// <para>
/// Applied with <see cref="System.Globalization.CultureInfo.InvariantCulture"/>. A value whose
/// rendering depends on the server's locale is a value that changes when the server moves.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwFormat("yyyy-MM-dd")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwFormatAttribute : DwPolicyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="format">A standard or custom .NET format string.</param>
    public DwFormatAttribute(string format) => Format = format;

    /// <summary>The format string applied to the value.</summary>
    public string Format { get; }

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
