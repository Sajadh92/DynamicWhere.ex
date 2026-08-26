using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Source;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// The whole gate: a <see cref="Filter"/> in, a sanitized <see cref="Filter"/> out.
/// </summary>
/// <remarks>
/// Pure by design. No database, no EF, no ambient state — which is what lets the riskiest logic in
/// the feature be tested exhaustively without a fixture. The sanitized clone is handed to the
/// existing extension methods unchanged; the pipeline never learns policy exists.
/// <para>
/// Every field path is canonicalized through the library's own <c>Validate&lt;T&gt;()</c> before
/// any policy is resolved. Phase 1 gave <c>PolicyFragment.NormalizePath</c> matching behaviour, but
/// two normalizers that agree only by construction is a standing liability: a fragment that fails
/// to match is a field left allowed. Calling the pipeline's own routine means there is one
/// canonical form, produced by one function.
/// </para>
/// </remarks>
internal static class FilterSanitizer
{
    /// <summary>
    /// Canonicalizes and gates a filter, returning a sanitized copy.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="resolver">Resolves the policy for one field.</param>
    /// <param name="context">The caller.</param>
    /// <param name="options">The enforcement posture.</param>
    /// <param name="trace">Collects what was decided.</param>
    /// <returns>A sanitized copy, safe to hand to the existing pipeline.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="LogicException">
    /// Thrown when a field path names nothing on <typeparamref name="T"/>. A field that does not
    /// exist has no policy, so it fails as validation before any policy decision is reached — and
    /// it fails identically whether the query is guarded or not, so the error discloses nothing
    /// about which fields a caller may see.
    /// </exception>
    internal static Filter Sanitize<T>(
        Filter filter,
        PolicyResolver resolver,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace)
        where T : class
    {
        if (filter is null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        if (resolver is null)
        {
            throw new ArgumentNullException(nameof(resolver));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (trace is null)
        {
            throw new ArgumentNullException(nameof(trace));
        }

        Filter working = filter.Clone();

        Canonicalize<T>(working);

        return working;
    }

    /// <summary>
    /// Rewrites every field path on the clone into the canonical form the pipeline uses.
    /// </summary>
    private static void Canonicalize<T>(Filter filter) where T : class
    {
        if (filter.ConditionGroup is not null)
        {
            CanonicalizeGroup<T>(filter.ConditionGroup);
        }

        if (filter.Selects is not null)
        {
            for (int i = 0; i < filter.Selects.Count; i++)
            {
                filter.Selects[i] = filter.Selects[i].Validate<T>();
            }
        }

        if (filter.Orders is not null)
        {
            foreach (OrderBy order in filter.Orders)
            {
                CanonicalizeOrder<T>(order);
            }
        }
    }

    /// <summary>
    /// Canonicalizes one condition group and every group beneath it.
    /// </summary>
    /// <remarks>
    /// Recursive because <see cref="ConditionGroup.SubConditionGroups"/> is. A walk that stopped at
    /// the top level would leave nested paths uncanonicalized, and an uncanonicalized path is one a
    /// deny fragment fails to match.
    /// </remarks>
    private static void CanonicalizeGroup<T>(ConditionGroup group) where T : class
    {
        if (group.Conditions is not null)
        {
            foreach (Condition condition in group.Conditions)
            {
                // Mirrors Validator's own check so a malformed condition fails the same way, with
                // the same code, whether or not the query is guarded.
                if (string.IsNullOrWhiteSpace(condition.Field))
                {
                    throw new LogicException(ErrorCode.InvalidField);
                }

                condition.Field = condition.Field!.Validate<T>();
            }
        }

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in group.SubConditionGroups)
            {
                CanonicalizeGroup<T>(sub);
            }
        }
    }

    /// <summary>
    /// Canonicalizes one order clause.
    /// </summary>
    private static void CanonicalizeOrder<T>(OrderBy order) where T : class
    {
        if (string.IsNullOrWhiteSpace(order.Field))
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        order.Field = order.Field!.Validate<T>();
    }
}
