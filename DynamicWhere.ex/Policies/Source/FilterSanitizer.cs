using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
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
    /// The field path reported when a refusal concerns a whole clause rather than one field.
    /// Matches the wildcard <c>PolicyFragment</c> already uses to mean "every field".
    /// </summary>
    private const string WholeClause = "*";

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
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
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

        Gate gate = new(typeof(T), resolver, context, options, trace);

        GateConditions(working.ConditionGroup, gate);
        GateOrders(working, gate);
        GateSelects(working, gate);

        return working;
    }

    /// <summary>
    /// Refuses every filter condition the policy denies, at any depth.
    /// </summary>
    /// <remarks>
    /// This throws in both tiers, and that asymmetry with projection and ordering is the point the
    /// whole design rests on. A dropped projection field returns less than was asked for; a dropped
    /// condition returns <em>more</em>. Silently removing a tenant predicate hands back every
    /// tenant's rows, so there is no posture in which quietly discarding a filter is the lenient
    /// option — it is the catastrophic one.
    /// <para>
    /// The walk is complete: every condition in every group, not the first of each. A gate that
    /// stopped early would be bypassed by ordering the conditions differently, or by nesting one
    /// level deeper than the walk reaches.
    /// </para>
    /// </remarks>
    private static void GateConditions(ConditionGroup? group, Gate gate)
    {
        if (group is null)
        {
            return;
        }

        if (group.Conditions is not null)
        {
            foreach (Condition condition in group.Conditions)
            {
                string field = condition.Field!;
                FieldPolicy policy = gate.PolicyFor(field);

                if (!policy.Allows(PolicyFeature.Where))
                {
                    gate.Deny(field, PolicyFeature.Where, PolicyErrorCode.FieldDeniedForWhere, policy);
                }
            }
        }

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in group.SubConditionGroups)
            {
                GateConditions(sub, gate);
            }
        }
    }

    /// <summary>
    /// Removes, or refuses, every sort field the policy denies.
    /// </summary>
    /// <remarks>
    /// An empty order list is meaningful where an empty projection is not: the pipeline returns
    /// the query unchanged, so the result is simply unordered. There is nothing to refuse.
    /// <para>
    /// Worth knowing when reading a result: pagination is applied after ordering, so a convenience
    /// tier that drops the only sort field leaves the page boundaries at the provider's discretion.
    /// The rows are still ones the caller may see; which page they land on stops being stable.
    /// </para>
    /// </remarks>
    private static void GateOrders(Filter filter, Gate gate)
    {
        if (filter.Orders is null || filter.Orders.Count == 0)
        {
            return;
        }

        List<OrderBy> kept = new(filter.Orders.Count);

        foreach (OrderBy order in filter.Orders)
        {
            string field = order.Field!;
            FieldPolicy policy = gate.PolicyFor(field);

            if (policy.Allows(PolicyFeature.Order))
            {
                kept.Add(order);

                continue;
            }

            gate.Refuse(field, PolicyFeature.Order, PolicyErrorCode.FieldDeniedForOrder, policy);
        }

        filter.Orders = kept;
    }

    /// <summary>
    /// Removes, or refuses, every projection field the policy denies.
    /// </summary>
    /// <remarks>
    /// Dropping is safe for a projection in a way it never is for a filter: a narrower projection
    /// returns less, where a narrower filter returns more. That asymmetry is why this tier check
    /// exists here and does not exist on the where clause.
    /// <para>
    /// Dropping every field is refused outright rather than left as an empty list. An empty list
    /// reaches the pipeline as an unrelated validation error, and a null one projects the whole
    /// entity — turning the strictest possible policy into the widest possible result.
    /// </para>
    /// </remarks>
    private static void GateSelects(Filter filter, Gate gate)
    {
        if (filter.Selects is null || filter.Selects.Count == 0)
        {
            SynthesizeSelects(filter, gate);

            return;
        }

        List<string> kept = new(filter.Selects.Count);

        foreach (string field in filter.Selects)
        {
            FieldPolicy policy = gate.PolicyFor(field);

            if (policy.Allows(PolicyFeature.Select))
            {
                kept.Add(field);

                continue;
            }

            gate.Refuse(field, PolicyFeature.Select, PolicyErrorCode.FieldDeniedForSelect, policy);
        }

        if (kept.Count == 0)
        {
            throw gate.Exception(WholeClause, PolicyFeature.Select, PolicyErrorCode.AllSelectsDenied, null);
        }

        filter.Selects = kept;
    }

    /// <summary>
    /// Builds a projection from the allowed fields when the caller sent none and something is
    /// denied.
    /// </summary>
    /// <remarks>
    /// A caller who sends no projection gets the whole entity, denied columns included, because
    /// the pipeline only projects when <c>Selects</c> is non-null. Gating the list alone would
    /// therefore enforce deny-select against precisely the callers who volunteered one, and leave
    /// it bypassable by asking for less.
    /// <para>
    /// Nothing is synthesized unless a field is actually denied. A type the policy has no opinion
    /// about keeps its null projection and generates the same SQL as the unguarded path, which is
    /// the difference between a policy layer that is invisible until it has something to say and
    /// one that rewrites every query in the application.
    /// </para>
    /// <para>
    /// Scalars only. A navigation is not loaded by an unguarded call in the first place, and
    /// projecting one whole would carry every field beneath it — reopening the same hole one level
    /// down. Where a caller had eagerly loaded one, narrowing it away fails closed.
    /// </para>
    /// <para>
    /// This never throws in the strict tier for a denied field. Strict refuses what a caller asks
    /// for, and here the caller named nothing; throwing would fail every strict query against a
    /// type carrying any denied field at all.
    /// </para>
    /// </remarks>
    private static void SynthesizeSelects(Filter filter, Gate gate)
    {
        List<string> allowed = new();
        bool anyDenied = false;

        foreach (string field in gate.ProjectableFields())
        {
            FieldPolicy policy = gate.PolicyFor(field);

            if (policy.Allows(PolicyFeature.Select))
            {
                allowed.Add(field);

                continue;
            }

            anyDenied = true;

            gate.Record(field, PolicyFeature.Select, PolicyAction.Dropped, policy);
        }

        if (!anyDenied)
        {
            return;
        }

        if (allowed.Count == 0)
        {
            throw gate.Exception(WholeClause, PolicyFeature.Select, PolicyErrorCode.AllSelectsDenied, null);
        }

        filter.Selects = allowed;
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

    /// <summary>
    /// The per-query state every gating step needs: who is asking, what the posture is, where
    /// decisions go, and the policies resolved so far.
    /// </summary>
    private sealed class Gate
    {
        private readonly Type _entityType;
        private readonly PolicyResolver _resolver;
        private readonly DwPolicyContext _context;
        private readonly DwPolicyOptions _options;
        private readonly PolicyTrace _trace;

        // Canonicalization has already run, so every key here is the exact canonical spelling and
        // an ordinal comparison is both correct and the cheaper one. A filter routinely names the
        // same field in a condition, an order, and a projection.
        private readonly Dictionary<string, FieldPolicy> _resolved = new(StringComparer.Ordinal);

        internal Gate(
            Type entityType,
            PolicyResolver resolver,
            DwPolicyContext context,
            DwPolicyOptions options,
            PolicyTrace trace)
        {
            _entityType = entityType;
            _resolver = resolver;
            _context = context;
            _options = options;
            _trace = trace;
        }

        /// <summary>True when every refusal throws rather than being applied quietly.</summary>
        internal bool IsStrict => _options.Tier == DwTier.Strict;

        /// <summary>
        /// The scalar properties of the entity that a typed projection can actually assign.
        /// </summary>
        /// <remarks>
        /// Read through the same reflection cache the validator uses, so a synthesized projection
        /// cannot name a field the pipeline would then reject. Write-only and indexed members are
        /// excluded because a typed projection assigns into them.
        /// </remarks>
        internal IEnumerable<string> ProjectableFields()
        {
            foreach (KeyValuePair<string, PropertyInfo> entry in CacheReflection.GetTypeProperties(_entityType))
            {
                PropertyInfo property = entry.Value;

                if (property.CanRead
                    && property.CanWrite
                    && property.GetIndexParameters().Length == 0
                    && CacheReflection.IsSimpleType(property.PropertyType))
                {
                    yield return property.Name;
                }
            }
        }

        /// <summary>Resolves one field's policy, once per query.</summary>
        internal FieldPolicy PolicyFor(string fieldPath)
        {
            if (!_resolved.TryGetValue(fieldPath, out FieldPolicy? policy))
            {
                policy = _resolver.Resolve(_entityType, fieldPath, _context);
                _resolved[fieldPath] = policy;
            }

            return policy;
        }

        /// <summary>
        /// Applies a refusal for a feature that may be dropped: throws in the strict tier, records
        /// the drop otherwise.
        /// </summary>
        internal void Refuse(string fieldPath, PolicyFeature feature, PolicyErrorCode code, FieldPolicy policy)
        {
            if (IsStrict)
            {
                Record(fieldPath, feature, PolicyAction.Denied, policy);

                throw Exception(fieldPath, feature, code, policy);
            }

            Record(fieldPath, feature, PolicyAction.Dropped, policy);
        }

        /// <summary>
        /// Applies a refusal for a feature that can never be dropped, because dropping it would
        /// widen the result set.
        /// </summary>
        internal void Deny(string fieldPath, PolicyFeature feature, PolicyErrorCode code, FieldPolicy policy)
        {
            Record(fieldPath, feature, PolicyAction.Denied, policy);

            throw Exception(fieldPath, feature, code, policy);
        }

        /// <summary>Records one decision against the query's trace.</summary>
        internal void Record(string fieldPath, PolicyFeature feature, PolicyAction action, FieldPolicy? policy)
        {
            _trace.Add(new PolicyDecision(fieldPath, feature, action, Describe(policy)));
        }

        /// <summary>Builds the refusal, attributing it only where the attribution is unambiguous.</summary>
        internal PolicyException Exception(
            string fieldPath,
            PolicyFeature feature,
            PolicyErrorCode code,
            FieldPolicy? policy)
        {
            // A resolved policy lists the winning source for every feature it decided, not for this
            // one alone, so naming a single source is only honest when there is a single source.
            // Per-feature attribution arrives with the explain endpoint; guessing here would put a
            // wrong rule id in front of an operator diagnosing a refusal.
            PolicySource? sole = policy is { Sources.Count: 1 } ? policy.Sources[0] : null;

            return new PolicyException(code, fieldPath, feature, _options.Tier)
            {
                RuleId = sole?.RuleId,
                SourceOrigin = sole?.Origin
            };
        }

        /// <summary>Names every source that contributed, for the trace.</summary>
        private static string? Describe(FieldPolicy? policy) =>
            policy is null || policy.Sources.Count == 0
                ? null
                : string.Join(", ", policy.Sources);
    }
}
