using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Requires the caller to filter on the decorated member. A request that does not throws in both
/// tiers.
/// </summary>
/// <remarks>
/// Naming the field is not enough. A caller sending <c>Status = A OR TenantId = 5</c> has mentioned
/// the required field and still receives every other tenant's rows matching <c>Status = A</c>, so
/// the requirement is satisfied only by a condition that actually narrows the result: one sitting in
/// a group whose connector is <c>And</c>, with every ancestor group up to the root also <c>And</c>.
/// <para>
/// The operator matters for the same reason. The default satisfying set is the positive membership
/// operators — <see cref="Operator.Equal"/>, <see cref="Operator.IEqual"/>, <see cref="Operator.In"/>
/// and <see cref="Operator.IIn"/> — because <see cref="Operator.NotEqual"/> and
/// <see cref="Operator.IsNull"/> each satisfy the letter of a scope while inverting it, and the
/// range operators widen it.
/// </para>
/// <para>
/// A <see cref="DwForceWhereAttribute"/> on the same member satisfies this requirement by supplying
/// the predicate itself. The two compose: force the scope, and require it so that a caller who
/// disables injection cannot proceed without one.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwRequireWhere]
/// public int TenantId { get; set; }
///
/// [DwRequireWhere(Operators = new[] { Operator.GreaterThanOrEqual })]
/// public DateTime OccurredAt { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwRequireWhereAttribute : DwPolicyAttribute
{
    /// <summary>
    /// The operators that satisfy this requirement when nothing overrides them.
    /// </summary>
    /// <remarks>
    /// Positive membership only. Every other operator either inverts the scope or widens it, and a
    /// requirement satisfied by a widening condition is a requirement that has been met on paper
    /// and defeated in fact.
    /// </remarks>
    public static IReadOnlyList<Operator> DefaultOperators { get; } =
        new[] { Operator.Equal, Operator.IEqual, Operator.In, Operator.IIn };

    /// <summary>
    /// The operators that satisfy this requirement, or null to use <see cref="DefaultOperators"/>.
    /// </summary>
    /// <remarks>
    /// Null and empty mean opposite things, exactly as they do on
    /// <see cref="DwOperatorsAttribute.Allow"/>. Null is "nothing was said", so the default set
    /// applies; empty is "this set, and it is empty", so nothing satisfies the requirement and every
    /// query on the type is refused. That is loud rather than silent, and it is the reading of an
    /// obvious typo that cannot grant access.
    /// </remarks>
    public Operator[]? Operators { get; set; }

    /// <summary>
    /// Reduces <see cref="Operators"/> to the set that satisfies this requirement.
    /// </summary>
    /// <returns>Every operator a condition may use to satisfy the requirement.</returns>
    public IReadOnlyList<Operator> Resolve() =>
        Operators is null ? DefaultOperators : Operators.Distinct().ToList();
}
