using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Adds a predicate on the decorated member to every guarded query, whether the caller asked for one
/// or not. The row-level scope: a tenant boundary, a soft-delete filter, an ownership check.
/// </summary>
/// <remarks>
/// The predicate is never gated against the caller's own policy. It is the library filtering on the
/// caller's behalf, so a member that is <see cref="DwNoWhereAttribute"/> for everyone can still carry
/// a forced predicate — indeed that is the usual pairing, since a caller who may not filter on a
/// tenant column is exactly the caller who must be confined to one.
/// <para>
/// The injected term always wraps the caller's condition group in a new <c>And</c> root; it is never
/// merged into it. Appending a tenant term inside a caller group of
/// <c>(Status = A OR Status = B)</c> produces <c>(Status = A OR Status = B OR TenantId = 5)</c>,
/// which returns every tenant's rows matching A or B. This is a correctness requirement, not an
/// implementation detail, and a test pins the shape.
/// </para>
/// <para>
/// Several forced predicates on one member all apply, joined by <c>And</c>. They are collected
/// rather than elected, unlike the effects, because a conjunction can only ever narrow — electing
/// one would let a low-authority rule silently discard a sealed scope.
/// </para>
/// <para>
/// The value's <see cref="DataType"/> is inferred from the member's own CLR type. There is no
/// override: C# forbids a nullable enum as an attribute argument, and an override could only ever
/// disagree with the type the pipeline is about to validate against.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwForceWhere(Operator.Equal, ContextValue = "TenantId")]   // from ctx.Values
/// public int TenantId { get; set; }
///
/// [DwForceWhere(Operator.Equal, Value = "false")]             // constant, soft delete
/// public bool IsDeleted { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
public sealed class DwForceWhereAttribute : DwPolicyAttribute
{
    /// <summary>
    /// Initializes the attribute.
    /// </summary>
    /// <param name="op">The operator the injected condition uses.</param>
    public DwForceWhereAttribute(Operator op) => Operator = op;

    /// <summary>The operator the injected condition uses.</summary>
    public Operator Operator { get; }

    /// <summary>
    /// A constant value, in string form, coerced by the pipeline's own normalizer.
    /// </summary>
    /// <remarks>
    /// Mutually exclusive with <see cref="ContextValue"/>. Setting neither leaves nothing to inject
    /// and setting both leaves no way to choose, so either is refused at resolution rather than
    /// resolved in favour of one.
    /// </remarks>
    public string? Value { get; set; }

    /// <summary>
    /// The key of an ambient value on the caller's context, such as <c>"TenantId"</c>.
    /// </summary>
    /// <remarks>
    /// A key the context does not supply, or supplies as null, throws in both tiers and in dry run.
    /// A tenant scope that silently fails to apply is worse than a failed request, and dry run's
    /// promise is that it changes no data — not that it grants access.
    /// </remarks>
    public string? ContextValue { get; set; }
}
