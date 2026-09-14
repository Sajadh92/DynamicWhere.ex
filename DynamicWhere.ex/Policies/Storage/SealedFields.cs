using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.ex.Policies.Storage;

/// <summary>
/// The configuration-time half of design section 5.7's sealed-field guarantee, shared by every
/// store.
/// </summary>
/// <remarks>
/// Section 5.7 says an operator cannot even attempt to grant a sealed field, enforced both at
/// configuration time and at resolution time. Resolution time is unconditional and is where the
/// guarantee actually lives: a sealed attribute outranks every dynamic level, so a rule aimed at
/// one loses whatever any store did or did not check. This is the other half — refusing the write
/// so the operator is told immediately, rather than believing they granted something.
/// <para>
/// Shared rather than reimplemented per store for the reason <see cref="PolicyPayload"/> and
/// <see cref="PolicyRuleDocument"/> are shared: three stores each writing their own check is three
/// checks, and the one that is subtly weaker is the one nobody compares. The conformance suite's
/// sealed-field test then proves the same code in all three, which is worth less than it looks
/// unless it really is the same code.
/// </para>
/// </remarks>
public static class SealedFields
{
    /// <summary>
    /// Refuses a rule aimed at a field a sealed attribute already speaks to.
    /// </summary>
    /// <param name="rule">The rule being written.</param>
    /// <param name="resolveType">
    /// Turns the rule's entity name into a type, or null when the store was given no resolver.
    /// </param>
    /// <param name="parameterName">The parameter to name in the exception.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the rule targets a sealed field.</exception>
    /// <remarks>
    /// Without a resolver this accepts, and that is not a hole: the rule loses at resolution and
    /// grants nothing. A store holds a string and the check needs a <c>Type</c>, so a store that
    /// cannot be handed one cannot perform it — which is why the resolution-time guarantee is
    /// tested separately, and why the weaker half must never be mistaken for the whole.
    /// </remarks>
    public static void Refuse(PolicyRule rule, Func<string, Type?>? resolveType, string parameterName)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        Type? type = resolveType?.Invoke(rule.EntityType);

        if (type is null)
        {
            return;
        }

        DwPolicyContext probe = new();

        foreach (PolicyFragment fragment in new AttributePolicyProvider().GetFragments(type, probe))
        {
            if (fragment.Level != PolicyLevel.SealedAttribute
                || !fragment.Matches(rule.FieldPath)
                || (fragment.Features & rule.Features) == 0)
            {
                continue;
            }

            throw new ArgumentException(
                $"'{rule.EntityType}.{rule.FieldPath}' is sealed by {fragment.Source.Origin} for " +
                $"{fragment.Features & rule.Features}. A rule cannot loosen it, so storing one " +
                "would record a grant that never takes effect.",
                parameterName);
        }
    }
}
