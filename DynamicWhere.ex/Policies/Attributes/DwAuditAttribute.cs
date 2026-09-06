using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Records every use of the decorated member to the configured audit sink.
/// </summary>
/// <remarks>
/// Both halves are recorded: an access that was allowed and one that was refused. A log holding
/// only refusals answers "who was stopped" and cannot answer "who read this", which is the question
/// an audit of a sensitive field exists to answer.
/// <para>
/// Events are buffered on the caller's context and drained once, after the response. The query path
/// is synchronous and a sink is not, so the alternatives were losing records to a fire-and-forget
/// call or blocking a request thread on I/O; neither is acceptable for a security record.
/// </para>
/// <para>
/// Sealed by default. A rule may widen the audit of a field, and may narrow or remove one only
/// where the author wrote <see cref="DwPolicyAttribute.Overridable"/>: a store that could switch
/// auditing off would leave the access happening with nothing written down.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwAudit]
/// public string NationalId { get; set; }
///
/// [DwAudit(PolicyFeature.Select)]
/// public string Email { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwAuditAttribute : DwPolicyAttribute
{
    /// <summary>Records every use of the field.</summary>
    public DwAuditAttribute() : this(PolicyFeature.All)
    {
    }

    /// <summary>
    /// Records only the named uses of the field.
    /// </summary>
    /// <param name="features">
    /// The features whose use is recorded. <see cref="PolicyFeature.None"/> is refused at fragment
    /// construction: an audit of nothing cannot be told apart from no audit at all.
    /// </param>
    public DwAuditAttribute(PolicyFeature features) => Features = features;

    /// <summary>The features whose use of this field is recorded.</summary>
    public PolicyFeature Features { get; }
}
