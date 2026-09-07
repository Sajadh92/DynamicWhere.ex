namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Shortens the decorated member's text to a maximum length.
/// </summary>
/// <remarks>
/// Runs last in the chain, so the length cap is the final word on what leaves the library however
/// long the preceding stages made the value. Text only: it emits text, so it is valid only where
/// text is assignable.
/// </remarks>
/// <example>
/// <code>
/// [DwTruncate(200, Ellipsis = "...")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwTruncateAttribute : DwPolicyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="length">The greatest number of characters kept, before any ellipsis.</param>
    public DwTruncateAttribute(int length) => Length = length;

    /// <summary>The greatest number of characters kept, before any ellipsis.</summary>
    public int Length { get; }

    /// <summary>Appended when the value was actually shortened, or null to append nothing.</summary>
    public string? Ellipsis { get; set; }

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
