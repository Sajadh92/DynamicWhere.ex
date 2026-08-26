using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
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

    /// <summary>The character separating one navigation segment from the next.</summary>
    private const char SegmentSeparator = '.';

    /// <summary>
    /// Canonicalizes and gates a filter, returning a sanitized copy.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="resolver">Resolves the policy for one field.</param>
    /// <param name="context">The caller.</param>
    /// <param name="options">The enforcement posture.</param>
    /// <param name="trace">Collects what was decided.</param>
    /// <param name="synthesizeProjection">
    /// When true, a null projection is replaced by the allowed fields if anything is denied. False
    /// when the caller is composing one clause at a time and sent no projection to speak of.
    /// </param>
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
        PolicyTrace trace,
        bool synthesizeProjection = true)
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

        EnforceCaps(working, gate);

        GateConditions(working.ConditionGroup, gate);
        GateOrders(working, gate);
        GateSelects(working, gate, synthesizeProjection);

        return working;
    }

    /// <summary>
    /// Canonicalizes and gates a summary, returning a sanitized copy.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="summary">The caller's summary. Never modified.</param>
    /// <param name="resolver">Resolves the policy for one field.</param>
    /// <param name="context">The caller.</param>
    /// <param name="options">The enforcement posture.</param>
    /// <param name="trace">Collects what was decided.</param>
    /// <returns>A sanitized copy, safe to hand to the existing pipeline.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the summary.</exception>
    /// <remarks>
    /// Only <c>GroupBy.Fields</c> and <c>AggregateBy.Field</c> are property paths, and only they
    /// are canonicalized here. <c>Having</c> and <c>Orders</c> name aggregate aliases and
    /// dot-stripped group-by keys, which reflection cannot resolve; they are gated through the
    /// fields they stand for instead.
    /// </remarks>
    internal static Summary Sanitize<T>(
        Summary summary,
        PolicyResolver resolver,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace)
        where T : class
    {
        if (summary is null)
        {
            throw new ArgumentNullException(nameof(summary));
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

        Summary working = summary.Clone();

        if (working.ConditionGroup is not null)
        {
            CanonicalizeGroup<T>(working.ConditionGroup);
        }

        CanonicalizeGrouping<T>(working.GroupBy);

        Gate gate = new(typeof(T), resolver, context, options, trace);

        EnforceCaps(working, gate);

        GateConditions(working.ConditionGroup, gate);
        GateGrouping(working.GroupBy, gate);

        // Built after grouping is canonical, because the references below stand for the canonical
        // paths and nothing else would map back to them.
        Dictionary<string, List<string>> references = BuildReferences(working.GroupBy);

        GateHaving(working.Having, references, gate);
        GateSummaryOrders(working, references, gate);

        return working;
    }

    /// <summary>
    /// Rewrites the grouping and aggregation paths into canonical form.
    /// </summary>
    private static void CanonicalizeGrouping<T>(GroupBy? groupBy) where T : class
    {
        if (groupBy is null)
        {
            return;
        }

        if (groupBy.Fields is not null)
        {
            for (int i = 0; i < groupBy.Fields.Count; i++)
            {
                groupBy.Fields[i] = groupBy.Fields[i].Validate<T>();
            }
        }

        if (groupBy.AggregateBy is not null)
        {
            foreach (AggregateBy aggregate in groupBy.AggregateBy)
            {
                // A Count needs no field, and an aggregate with no field has no underlying policy.
                if (!string.IsNullOrWhiteSpace(aggregate.Field))
                {
                    aggregate.Field = aggregate.Field!.Validate<T>();
                }
            }
        }
    }

    /// <summary>
    /// Refuses every grouping key and aggregated field the policy denies.
    /// </summary>
    /// <remarks>
    /// Both throw in both tiers. A grouping key cannot be dropped the way a sort can: removing one
    /// collapses rows together and changes every aggregate in the result, so the caller would get
    /// numbers that answer a different question than the one they asked. Dropping an aggregate
    /// would silently delete a column the caller is about to read by alias.
    /// </remarks>
    private static void GateGrouping(GroupBy? groupBy, Gate gate)
    {
        if (groupBy is null)
        {
            return;
        }

        if (groupBy.Fields is not null)
        {
            foreach (string field in groupBy.Fields)
            {
                FieldPolicy policy = gate.PolicyFor(field);

                if (!policy.Allows(PolicyFeature.Group))
                {
                    gate.Deny(field, PolicyFeature.Group, PolicyErrorCode.FieldDeniedForGroup, policy);
                }
            }
        }

        if (groupBy.AggregateBy is not null)
        {
            foreach (AggregateBy aggregate in groupBy.AggregateBy)
            {
                if (string.IsNullOrWhiteSpace(aggregate.Field))
                {
                    continue;
                }

                string field = aggregate.Field!;
                FieldPolicy policy = gate.PolicyFor(field);

                if (!policy.Allows(PolicyFeature.Aggregate))
                {
                    gate.Deny(field, PolicyFeature.Aggregate, PolicyErrorCode.FieldDeniedForAggregate, policy);
                }
            }
        }
    }

    /// <summary>
    /// Maps every name a <c>Having</c> or <c>Orders</c> clause may use back to the property paths
    /// it stands for.
    /// </summary>
    /// <remarks>
    /// The validator accepts three spellings in those clauses, and only one of them is a property
    /// path: a group-by key as written (<c>"Contact.Phone"</c>), the same key with its dots
    /// stripped (<c>"ContactPhone"</c>, which is the alias the projection actually emits), and an
    /// aggregate alias. A gate that knew only about property paths and aliases would wave the
    /// stripped form straight through.
    /// <para>
    /// A name maps to a <em>list</em> because two names can legitimately collide before the
    /// validator has run: <c>"A.B"</c> and <c>"AB"</c> strip to the same thing, and duplicate
    /// aliases are rejected downstream rather than here. Gating every path a name could mean is the
    /// only resolution that cannot fail open.
    /// </para>
    /// <para>
    /// Comparison is <see cref="StringComparer.OrdinalIgnoreCase"/>, matching the validator's own
    /// sets. A stricter comparison here would let a differently-cased alias miss the gate, and a
    /// reference that misses the gate is a field left allowed.
    /// </para>
    /// </remarks>
    private static Dictionary<string, List<string>> BuildReferences(GroupBy? groupBy)
    {
        Dictionary<string, List<string>> references = new(StringComparer.OrdinalIgnoreCase);

        if (groupBy is null)
        {
            return references;
        }

        if (groupBy.Fields is not null)
        {
            foreach (string field in groupBy.Fields)
            {
                Map(references, field, field);
                Map(references, field.Replace(".", string.Empty), field);
            }
        }

        if (groupBy.AggregateBy is not null)
        {
            foreach (AggregateBy aggregate in groupBy.AggregateBy)
            {
                // A Count carries no field. There is no underlying policy to inherit, so the alias
                // stands for nothing and is left alone.
                if (!string.IsNullOrWhiteSpace(aggregate.Alias)
                    && !string.IsNullOrWhiteSpace(aggregate.Field))
                {
                    Map(references, aggregate.Alias!, aggregate.Field!);
                }
            }
        }

        return references;
    }

    /// <summary>Records that one reference name stands for one property path.</summary>
    private static void Map(Dictionary<string, List<string>> references, string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (!references.TryGetValue(name, out List<string>? paths))
        {
            paths = new List<string>();
            references[name] = paths;
        }

        if (!paths.Contains(path, StringComparer.Ordinal))
        {
            paths.Add(path);
        }
    }

    /// <summary>
    /// Refuses a <c>Having</c> clause that filters, through an alias, on a field denied for
    /// filtering.
    /// </summary>
    /// <remarks>
    /// A having clause is a filter, so it throws in both tiers for the same reason the where clause
    /// does. The attack it closes is specific: a field can be allowed for aggregation and refused
    /// for filtering, and <c>HAVING SUM(Salary) &gt; n</c> then answers the question the where
    /// clause was not allowed to ask. Repeat it and the answer narrows to a value.
    /// <para>
    /// A reference matching nothing is left exactly as it is. The validator rejects it downstream
    /// with its own error, which keeps the guarded and unguarded failures identical.
    /// </para>
    /// </remarks>
    private static void GateHaving(
        ConditionGroup? having,
        Dictionary<string, List<string>> references,
        Gate gate)
    {
        if (having is null)
        {
            return;
        }

        if (having.Conditions is not null)
        {
            foreach (Condition condition in having.Conditions)
            {
                if (condition.Field is null
                    || !references.TryGetValue(condition.Field, out List<string>? paths))
                {
                    continue;
                }

                foreach (string path in paths)
                {
                    FieldPolicy policy = gate.PolicyFor(path);

                    if (!policy.Allows(PolicyFeature.Where))
                    {
                        gate.Deny(
                            path, PolicyFeature.Where, PolicyErrorCode.FieldDeniedForWhere,
                            policy, condition.Field);
                    }

                    // An aggregate does not launder an operator restriction. HAVING MAX(NationalId)
                    // LIKE '12%' searches the column exactly as a where clause would, so the alias
                    // inherits the restriction along with the refusal.
                    if (!policy.AllowsOperator(condition.Operator))
                    {
                        gate.Deny(
                            path, PolicyFeature.Where, PolicyErrorCode.OperatorNotAllowed,
                            policy, condition.Field);
                    }
                }
            }
        }

        if (having.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in having.SubConditionGroups)
            {
                GateHaving(sub, references, gate);
            }
        }
    }

    /// <summary>
    /// Removes, or refuses, a summary sort that reaches a field denied for sorting.
    /// </summary>
    /// <remarks>
    /// Sorting by an aggregate of a field discloses the ordering of that field, so the reference
    /// inherits the refusal of whatever it stands for. Droppable in the convenience tier, like any
    /// other sort.
    /// </remarks>
    private static void GateSummaryOrders(
        Summary summary,
        Dictionary<string, List<string>> references,
        Gate gate)
    {
        if (summary.Orders is null || summary.Orders.Count == 0)
        {
            return;
        }

        List<OrderBy> kept = new(summary.Orders.Count);

        foreach (OrderBy order in summary.Orders)
        {
            if (order.Field is null || !references.TryGetValue(order.Field, out List<string>? paths))
            {
                kept.Add(order);

                continue;
            }

            bool allowed = true;

            foreach (string path in paths)
            {
                FieldPolicy policy = gate.PolicyFor(path);

                if (policy.Allows(PolicyFeature.Order))
                {
                    continue;
                }

                allowed &= !gate.Refuse(
                    path, PolicyFeature.Order, PolicyErrorCode.FieldDeniedForOrder, policy, order.Field);
            }

            if (allowed)
            {
                kept.Add(order);
            }
        }

        summary.Orders = kept;
    }

    /// <summary>
    /// Canonicalizes and gates a segment, returning a sanitized copy.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="segment">The caller's segment. Never modified.</param>
    /// <param name="resolver">Resolves the policy for one field.</param>
    /// <param name="context">The caller.</param>
    /// <param name="options">The enforcement posture.</param>
    /// <param name="trace">Collects what was decided.</param>
    /// <returns>A sanitized copy, safe to hand to the existing pipeline.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the segment.</exception>
    /// <remarks>
    /// Every condition set is gated independently, because each one becomes its own subquery and a
    /// field refused in one must not be reachable through another.
    /// <para>
    /// A field refused for <see cref="PolicyFeature.Segment"/> is refused anywhere inside a segment,
    /// in either tier. Union, Intersect and Except disclose through set membership rather than
    /// through the columns they return, so narrowing a projection does not help: whether a row
    /// survives an Except already answers the question the projection was hiding.
    /// </para>
    /// </remarks>
    internal static Segment Sanitize<T>(
        Segment segment,
        PolicyResolver resolver,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace)
        where T : class
    {
        if (segment is null)
        {
            throw new ArgumentNullException(nameof(segment));
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

        Segment working = segment.Clone();

        if (working.ConditionSets is not null)
        {
            foreach (ConditionSet set in working.ConditionSets)
            {
                if (set.ConditionGroup is not null)
                {
                    CanonicalizeGroup<T>(set.ConditionGroup);
                }
            }
        }

        if (working.Selects is not null)
        {
            for (int i = 0; i < working.Selects.Count; i++)
            {
                working.Selects[i] = working.Selects[i].Validate<T>();
            }
        }

        if (working.Orders is not null)
        {
            foreach (OrderBy order in working.Orders)
            {
                CanonicalizeOrder<T>(order);
            }
        }

        Gate gate = new(typeof(T), resolver, context, options, trace);

        EnforceCaps(working, gate);
        GateSegmentParticipation(working, gate);

        if (working.ConditionSets is not null)
        {
            foreach (ConditionSet set in working.ConditionSets)
            {
                GateConditions(set.ConditionGroup, gate);
            }
        }

        GateSegmentOrders(working, gate);
        GateSegmentSelects(working, gate);

        return working;
    }

    /// <summary>
    /// Refuses any field the policy keeps out of a set operation entirely.
    /// </summary>
    private static void GateSegmentParticipation(Segment segment, Gate gate)
    {
        foreach (string field in SegmentFields(segment))
        {
            FieldPolicy policy = gate.PolicyFor(field);

            if (!policy.Allows(PolicyFeature.Segment))
            {
                gate.Deny(field, PolicyFeature.Segment, PolicyErrorCode.FieldDeniedForSegment, policy);
            }
        }
    }

    /// <summary>Every field path a segment names, at any depth, in any clause.</summary>
    private static IEnumerable<string> SegmentFields(Segment segment)
    {
        if (segment.ConditionSets is not null)
        {
            foreach (ConditionSet set in segment.ConditionSets)
            {
                foreach (string field in GroupFields(set.ConditionGroup))
                {
                    yield return field;
                }
            }
        }

        if (segment.Selects is not null)
        {
            foreach (string field in segment.Selects)
            {
                yield return field;
            }
        }

        if (segment.Orders is not null)
        {
            foreach (OrderBy order in segment.Orders)
            {
                if (!string.IsNullOrWhiteSpace(order.Field))
                {
                    yield return order.Field!;
                }
            }
        }
    }

    /// <summary>Every field path a condition group names, at any depth.</summary>
    private static IEnumerable<string> GroupFields(ConditionGroup? group)
    {
        if (group is null)
        {
            yield break;
        }

        if (group.Conditions is not null)
        {
            foreach (Condition condition in group.Conditions)
            {
                if (!string.IsNullOrWhiteSpace(condition.Field))
                {
                    yield return condition.Field!;
                }
            }
        }

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in group.SubConditionGroups)
            {
                foreach (string field in GroupFields(sub))
                {
                    yield return field;
                }
            }
        }
    }

    /// <summary>Removes, or refuses, every segment sort field the policy denies.</summary>
    private static void GateSegmentOrders(Segment segment, Gate gate)
    {
        if (segment.Orders is null || segment.Orders.Count == 0)
        {
            return;
        }

        List<OrderBy> kept = new(segment.Orders.Count);

        foreach (OrderBy order in segment.Orders)
        {
            string field = order.Field!;
            FieldPolicy policy = gate.PolicyFor(field);

            if (policy.Allows(PolicyFeature.Order)
                || !gate.Refuse(field, PolicyFeature.Order, PolicyErrorCode.FieldDeniedForOrder, policy))
            {
                kept.Add(order);
            }
        }

        segment.Orders = kept;
    }

    /// <summary>
    /// Removes, or refuses, every segment projection field the policy denies.
    /// </summary>
    /// <remarks>
    /// No projection is synthesized here. A segment with no selects composes whole rows, and
    /// narrowing the projection would not close the disclosure anyway — participation is what
    /// <see cref="GateSegmentParticipation"/> refuses.
    /// </remarks>
    private static void GateSegmentSelects(Segment segment, Gate gate)
    {
        if (segment.Selects is null || segment.Selects.Count == 0)
        {
            return;
        }

        List<string> kept = new(segment.Selects.Count);

        foreach (string field in segment.Selects)
        {
            FieldPolicy policy = gate.PolicyFor(field);

            if (policy.Allows(PolicyFeature.Select)
                || !gate.Refuse(field, PolicyFeature.Select, PolicyErrorCode.FieldDeniedForSelect, policy))
            {
                kept.Add(field);
            }
        }

        if (kept.Count == 0)
        {
            throw gate.Exception(WholeClause, PolicyFeature.Select, PolicyErrorCode.AllSelectsDenied, null);
        }

        segment.Selects = kept;
    }

    /// <summary>
    /// Refuses a segment that exceeds a configured limit.
    /// </summary>
    /// <remarks>
    /// Conditions are counted across every set together. Each set becomes its own subquery, so the
    /// budget has to cover their sum or a caller buys the whole limit once per set.
    /// </remarks>
    private static void EnforceCaps(Segment segment, Gate gate)
    {
        int conditions = 0;

        if (segment.ConditionSets is not null)
        {
            foreach (ConditionSet set in segment.ConditionSets)
            {
                conditions += CountConditions(set.ConditionGroup);
            }
        }

        gate.CheckConditionCount(conditions);
        gate.CheckOrderCount(segment.Orders?.Count ?? 0);
        gate.CheckPage(segment.Page);

        foreach (string field in SegmentFields(segment))
        {
            gate.CheckDepth(field);
        }
    }

    /// <summary>
    /// Refuses a filter that exceeds a configured limit.
    /// </summary>
    /// <remarks>
    /// Run before any policy is resolved. Caps exist to stop a request generating unbounded work,
    /// and resolving a policy for every field of an unbounded filter is exactly the work being
    /// avoided, so the cheap structural check has to come first.
    /// <para>
    /// Every cap refuses in both tiers, and none of them clamps. Truncating a filter would widen
    /// its result the way dropping a condition does, and silently shrinking a page would make a
    /// caller stepping through results skip rows while every call reported success.
    /// </para>
    /// </remarks>
    private static void EnforceCaps(Filter filter, Gate gate)
    {
        gate.CheckConditionCount(CountConditions(filter.ConditionGroup));
        gate.CheckOrderCount(filter.Orders?.Count ?? 0);
        gate.CheckPage(filter.Page);

        CheckDepth(filter.ConditionGroup, gate);

        if (filter.Selects is not null)
        {
            foreach (string field in filter.Selects)
            {
                gate.CheckDepth(field);
            }
        }

        if (filter.Orders is not null)
        {
            foreach (OrderBy order in filter.Orders)
            {
                gate.CheckDepth(order.Field);
            }
        }
    }

    /// <summary>
    /// Refuses a summary that exceeds a configured limit.
    /// </summary>
    /// <remarks>
    /// The condition budget covers the where clause and the having clause together: both compile
    /// into predicates on the same query, so counting them apart would let a caller spend the
    /// budget twice.
    /// </remarks>
    private static void EnforceCaps(Summary summary, Gate gate)
    {
        gate.CheckConditionCount(CountConditions(summary.ConditionGroup) + CountConditions(summary.Having));
        gate.CheckOrderCount(summary.Orders?.Count ?? 0);
        gate.CheckPage(summary.Page);

        CheckDepth(summary.ConditionGroup, gate);

        if (summary.GroupBy?.Fields is not null)
        {
            foreach (string field in summary.GroupBy.Fields)
            {
                gate.CheckDepth(field);
            }
        }

        if (summary.GroupBy?.AggregateBy is not null)
        {
            foreach (AggregateBy aggregate in summary.GroupBy.AggregateBy)
            {
                gate.CheckDepth(aggregate.Field);
            }
        }
    }

    /// <summary>
    /// Counts every condition in a group and everything nested beneath it.
    /// </summary>
    /// <remarks>
    /// Counting the root group alone would let any budget be evaded by nesting, which is the
    /// cheapest way to rebuild the unbounded join the cap exists to prevent.
    /// </remarks>
    private static int CountConditions(ConditionGroup? group)
    {
        if (group is null)
        {
            return 0;
        }

        int count = group.Conditions?.Count ?? 0;

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in group.SubConditionGroups)
            {
                count += CountConditions(sub);
            }
        }

        return count;
    }

    /// <summary>Checks the navigation depth of every condition in a group tree.</summary>
    private static void CheckDepth(ConditionGroup? group, Gate gate)
    {
        if (group is null)
        {
            return;
        }

        if (group.Conditions is not null)
        {
            foreach (Condition condition in group.Conditions)
            {
                gate.CheckDepth(condition.Field);
            }
        }

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in group.SubConditionGroups)
            {
                CheckDepth(sub, gate);
            }
        }
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

                // Checked after the feature, so a field refused outright reports that rather than
                // an operator complaint that would imply filtering it is otherwise fine.
                if (!policy.AllowsOperator(condition.Operator))
                {
                    gate.Deny(field, PolicyFeature.Where, PolicyErrorCode.OperatorNotAllowed, policy);
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

            if (policy.Allows(PolicyFeature.Order)
                || !gate.Refuse(field, PolicyFeature.Order, PolicyErrorCode.FieldDeniedForOrder, policy))
            {
                kept.Add(order);
            }
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
    private static void GateSelects(Filter filter, Gate gate, bool synthesize)
    {
        if (filter.Selects is null || filter.Selects.Count == 0)
        {
            // Skipped when the caller is composing one clause at a time rather than sending a whole
            // filter: a lone Where or Order carries no projection, and synthesizing one for it would
            // refuse the call outright on a type whose every field is denied for select.
            if (synthesize)
            {
                SynthesizeSelects(filter, gate);
            }

            return;
        }

        List<string> kept = new(filter.Selects.Count);

        foreach (string field in filter.Selects)
        {
            FieldPolicy policy = gate.PolicyFor(field);

            if (policy.Allows(PolicyFeature.Select)
                || !gate.Refuse(field, PolicyFeature.Select, PolicyErrorCode.FieldDeniedForSelect, policy))
            {
                kept.Add(field);
            }
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

        if (!anyDenied || gate.IsDryRun)
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
        /// True when nothing is enforced and every decision is only recorded.
        /// </summary>
        /// <remarks>
        /// The union of two switches. The global one turns enforcement off everywhere; the
        /// per-context one turns it off for a single caller, which is what lets one canary subject
        /// run unenforced while everyone else stays enforced. An all-or-nothing global flag would
        /// force the rollout to be all-or-nothing too.
        /// </remarks>
        internal bool IsDryRun => _options.DryRun || _context.DryRun;

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

        /// <summary>Refuses a filter holding more conditions than the budget allows.</summary>
        internal void CheckConditionCount(int count)
        {
            if (count > _options.Caps.MaxConditions)
            {
                Raise(Cap("MaxConditions", _options.Caps.MaxConditions, count, WholeClause));
            }
        }

        /// <summary>Refuses a query sorting by more fields than the budget allows.</summary>
        internal void CheckOrderCount(int count)
        {
            if (count > _options.Caps.MaxOrderFields)
            {
                Raise(Cap("MaxOrderFields", _options.Caps.MaxOrderFields, count, WholeClause));
            }
        }

        /// <summary>Refuses a page larger than the budget allows.</summary>
        internal void CheckPage(PageBy? page)
        {
            if (page is not null && page.PageSize > _options.Caps.MaxPageSize)
            {
                Raise(Cap("MaxPageSize", _options.Caps.MaxPageSize, page.PageSize, WholeClause));
            }
        }

        /// <summary>
        /// Refuses a field path traversing more navigations than the budget allows.
        /// </summary>
        /// <remarks>
        /// Measured in segments, which matches the depth the attribute provider walks to, so the
        /// provider can never emit a fragment for a path this would then reject. A self-referencing
        /// type otherwise lets a caller write a path of any length and make the provider emit one
        /// join per segment.
        /// </remarks>
        internal void CheckDepth(string? fieldPath)
        {
            if (string.IsNullOrEmpty(fieldPath))
            {
                return;
            }

            // Canonicalization has already collapsed empty segments, so counting separators gives
            // the same count Validate<T>() walked.
            int segments = 1;

            for (int i = 0; i < fieldPath!.Length; i++)
            {
                if (fieldPath[i] == SegmentSeparator)
                {
                    segments++;
                }
            }

            if (segments > _options.Caps.MaxNavigationDepth)
            {
                Raise(Cap("MaxNavigationDepth", _options.Caps.MaxNavigationDepth, segments, fieldPath));
            }
        }

        /// <summary>Throws a cap refusal, unless the dry run suppressed it.</summary>
        private static void Raise(PolicyException? refusal)
        {
            if (refusal is not null)
            {
                throw refusal;
            }
        }

        /// <summary>Records and builds a cap refusal, or records only in a dry run.</summary>
        /// <remarks>
        /// The feature is <see cref="PolicyFeature.None"/> because a cap is structural: it is about
        /// the size of the request, not about what the caller may do with a field. The cap that
        /// fired is named in <c>SourceOrigin</c>, since it is the thing that decided and there is
        /// otherwise no way to tell which limit was hit.
        /// </remarks>
        private PolicyException? Cap(string name, int limit, int actual, string fieldPath)
        {
            string origin = $"{name} cap ({limit}), request had {actual}";

            _trace.Add(new PolicyDecision(fieldPath, PolicyFeature.None, PolicyAction.Denied, origin));

            if (IsDryRun)
            {
                return null;
            }

            return new PolicyException(PolicyErrorCode.CapExceeded, fieldPath, PolicyFeature.None, _options.Tier)
            {
                SourceOrigin = origin
            };
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
        /// <returns>
        /// True when the caller must remove the clause. False in a dry run, where the decision is
        /// recorded and the query is left exactly as the caller wrote it.
        /// </returns>
        internal bool Refuse(
            string fieldPath,
            PolicyFeature feature,
            PolicyErrorCode code,
            FieldPolicy policy,
            string? via = null)
        {
            Record(fieldPath, feature, IsStrict ? PolicyAction.Denied : PolicyAction.Dropped, policy, via);

            if (IsDryRun)
            {
                return false;
            }

            if (IsStrict)
            {
                throw Exception(fieldPath, feature, code, policy);
            }

            return true;
        }

        /// <summary>
        /// Applies a refusal for a feature that can never be dropped, because dropping it would
        /// widen the result set.
        /// </summary>
        internal void Deny(
            string fieldPath,
            PolicyFeature feature,
            PolicyErrorCode code,
            FieldPolicy policy,
            string? via = null)
        {
            Record(fieldPath, feature, PolicyAction.Denied, policy, via);

            if (IsDryRun)
            {
                return;
            }

            throw Exception(fieldPath, feature, code, policy);
        }

        /// <summary>Records one decision against the query's trace.</summary>
        internal void Record(
            string fieldPath,
            PolicyFeature feature,
            PolicyAction action,
            FieldPolicy? policy,
            string? via = null)
        {
            // The decision names the field the policy governs, not the alias the caller wrote, so
            // a trace can be read straight against the policy. The alias goes in the reason,
            // because otherwise the caller cannot tell which part of their query was refused.
            string? reason = Describe(policy);

            if (via is not null && !string.Equals(via, fieldPath, StringComparison.Ordinal))
            {
                reason = reason is null ? "via '" + via + "'" : reason + " (via '" + via + "')";
            }

            _trace.Add(new PolicyDecision(fieldPath, feature, action, reason));
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
