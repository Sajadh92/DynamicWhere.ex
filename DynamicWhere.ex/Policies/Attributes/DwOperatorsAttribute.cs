using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Restricts which operators may be used against the decorated member.
/// </summary>
/// <remarks>
/// This is the control that stops enumeration, and it is the reason refusing a field outright is
/// not the only useful answer. A national identifier that permits <see cref="Operator.Equal"/> and
/// <see cref="Operator.In"/> but refuses <see cref="Operator.Contains"/> and
/// <see cref="Operator.StartsWith"/> can be confirmed by a caller who already holds the value and
/// cannot be discovered by one who does not: substring matching turns a single column into an
/// oracle a caller can walk one character at a time, narrowing to the whole value in a few hundred
/// queries.
/// <para>
/// A restriction is not an effect. It never elects a winner against other fragments the way
/// <see cref="DwDenyAttribute"/> does — every restriction that matches a field applies, and the
/// permitted set is their intersection, so an additional rule can only ever narrow it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwOperators(Allow = new[] { Operator.Equal, Operator.In })]
/// public string NationalId { get; set; }
///
/// [DwOperators(Deny = new[] { Operator.Contains })]
/// public string Iban { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
public sealed class DwOperatorsAttribute : DwPolicyAttribute
{
    /// <summary>
    /// The only operators permitted. When set, anything absent from it is refused.
    /// </summary>
    public Operator[]? Allow { get; set; }

    /// <summary>
    /// Operators refused. Everything else is permitted.
    /// </summary>
    public Operator[]? Deny { get; set; }

    /// <summary>
    /// Reduces <see cref="Allow"/> and <see cref="Deny"/> to the single set of permitted operators.
    /// </summary>
    /// <remarks>
    /// Both forms collapse to an allow-list so that combining restrictions is an intersection, and
    /// an intersection can only narrow. Setting both properties is a contradiction, and it is
    /// resolved against the caller: <see cref="Deny"/> is subtracted from <see cref="Allow"/>, so
    /// an operator named in both is refused.
    /// </remarks>
    /// <returns>Every operator this attribute permits.</returns>
    public IReadOnlyList<Operator> Resolve()
    {
        IEnumerable<Operator> allowed = Allow ?? (IEnumerable<Operator>)Enum.GetValues<Operator>();

        if (Deny is { Length: > 0 })
        {
            allowed = allowed.Where(op => Array.IndexOf(Deny, op) < 0);
        }

        return allowed.Distinct().ToList();
    }
}
