using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Audit;
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

        // The gate is built first now, because canonicalizing a name needs the type's alias map and
        // the gate is what carries it. Nothing the gate does depends on the paths being canonical.
        Gate gate = new(typeof(T), resolver, context, options, trace);

        Canonicalize<T>(working, gate);

        EnforceCaps(working, gate);
        EnforceCost(working, gate);

        GateConditions(working.ConditionGroup, gate);
        GateOrders(working, gate);
        GateSelects(working, gate, synthesizeProjection);

        // Last, and after gating rather than before it. A forced predicate is the library filtering
        // on the caller's behalf, so it is never checked against the caller's own policy — and
        // running it through the gate would refuse the scope on exactly the field it exists for.
        working.ConditionGroup = Inject<T>(working.ConditionGroup, gate);

        // After injection, so that forcing a scope and requiring it compose: the library supplies
        // the predicate and the same walk that checks the caller's conditions sees it.
        VerifyRequired(working.ConditionGroup, gate);

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

        Gate gate = new(typeof(T), resolver, context, options, trace);

        if (working.ConditionGroup is not null)
        {
            CanonicalizeGroup<T>(working.ConditionGroup, gate);
        }

        CanonicalizeGrouping<T>(working.GroupBy, gate);

        EnforceCaps(working, gate);
        EnforceCost(working, gate);

        GateConditions(working.ConditionGroup, gate);
        GateGrouping(working.GroupBy, gate);

        // Built after grouping is canonical, because the references below stand for the canonical
        // paths and nothing else would map back to them.
        Dictionary<string, List<string>> references = BuildReferences(working.GroupBy, gate);

        GateHaving(working.Having, references, gate);
        GateSummaryOrders(working, references, gate);

        // A summary is a query that returns rows, so it takes the same scope a filter does.
        // Injecting on the filter path alone would let a caller read an aggregate across every
        // tenant simply by asking for a grouped result instead of a list.
        working.ConditionGroup = Inject<T>(working.ConditionGroup, gate);

        VerifyRequired(working.ConditionGroup, gate);

        // Last, and after gating, exactly as a forced predicate is. This is the library counting on
        // its own behalf: the floor needs each group's size and a caller asking for a maximum has
        // not supplied one. Running it through the gate would check the caller's policy against a
        // column they did not write, and charging it to the budget would bill them for a control
        // that exists to protect the data from them.
        try
        {
            GroupFloor.Inject(
                working, GroupFloor.For(working, gate.TypePolicy, gate.Options), gate.IsDryRun, trace);
        }
        catch (ArgumentException taken)
        {
            throw new PolicyException(
                PolicyErrorCode.GroupTooSmall, WholeClause, PolicyFeature.Aggregate, options.Tier)
            {
                SourceOrigin =
                    $"the alias '{taken.Message}' is reserved for the group-size floor this summary "
                    + "needs, and the summary already uses it"
            };
        }

        return working;
    }

    /// <summary>
    /// Rewrites the grouping and aggregation paths into canonical form.
    /// </summary>
    private static void CanonicalizeGrouping<T>(GroupBy? groupBy, Gate gate) where T : class
    {
        if (groupBy is null)
        {
            return;
        }

        if (groupBy.Fields is not null)
        {
            for (int i = 0; i < groupBy.Fields.Count; i++)
            {
                groupBy.Fields[i] = ResolveName<T>(groupBy.Fields[i], gate);
            }
        }

        if (groupBy.AggregateBy is not null)
        {
            foreach (AggregateBy aggregate in groupBy.AggregateBy)
            {
                // A Count needs no field, and an aggregate with no field has no underlying policy.
                if (!string.IsNullOrWhiteSpace(aggregate.Field))
                {
                    aggregate.Field = ResolveName<T>(aggregate.Field!, gate);
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
                FieldPolicy policy = gate.PolicyFor(field, PolicyFeature.Group);

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
                FieldPolicy policy = gate.PolicyFor(field, PolicyFeature.Aggregate);

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
    private static Dictionary<string, List<string>> BuildReferences(GroupBy? groupBy, Gate gate)
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
                MapAliases(references, field, gate);
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

    /// <summary>
    /// Records every public name that stands for one grouping key.
    /// </summary>
    /// <remarks>
    /// The fourth spelling, and the reason it is easy to miss. A caller who groups by
    /// <c>customer_name</c> has that key rewritten to its canonical path before the references are
    /// built, and then writes <c>customer_name</c> again in <c>Orders</c> or <c>Having</c> — where
    /// nothing would recognize it. A reference that matches nothing is skipped by the gate, and a
    /// clause the gate skips is a field left allowed.
    /// <para>
    /// Every alias standing for the key is mapped, not only the one this caller happened to use, so
    /// a second name for the same field cannot slip past either.
    /// </para>
    /// </remarks>
    private static void MapAliases(Dictionary<string, List<string>> references, string field, Gate gate)
    {
        foreach (KeyValuePair<string, IReadOnlyList<string>> entry in gate.TypePolicy.Aliases)
        {
            if (entry.Value.Contains(field, StringComparer.OrdinalIgnoreCase))
            {
                Map(references, entry.Key, field);
            }
        }
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
                    FieldPolicy policy = gate.PolicyFor(path, PolicyFeature.Where);

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
                FieldPolicy policy = gate.PolicyFor(path, PolicyFeature.Order);

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

        Gate gate = new(typeof(T), resolver, context, options, trace);

        if (working.ConditionSets is not null)
        {
            foreach (ConditionSet set in working.ConditionSets)
            {
                if (set.ConditionGroup is not null)
                {
                    CanonicalizeGroup<T>(set.ConditionGroup, gate);
                }
            }
        }

        if (working.Selects is not null)
        {
            for (int i = 0; i < working.Selects.Count; i++)
            {
                working.Selects[i] = ResolveName<T>(working.Selects[i], gate);
            }
        }

        if (working.Orders is not null)
        {
            foreach (OrderBy order in working.Orders)
            {
                CanonicalizeOrder<T>(order, gate);
            }
        }

        EnforceCaps(working, gate);
        EnforceCost(working, gate);
        GateSegmentParticipation(working, gate);
        GateSegmentInference(working, gate);

        if (working.ConditionSets is not null)
        {
            foreach (ConditionSet set in working.ConditionSets)
            {
                GateConditions(set.ConditionGroup, gate);
            }
        }

        GateSegmentOrders(working, gate);
        GateSegmentSelects(working, gate);

        InjectIntoSets<T>(working, gate);

        // Every set, independently. One unscoped arm of a set operation is enough: Union hands the
        // unscoped rows back directly, and Except hands back their complement.
        if (working.ConditionSets is null || working.ConditionSets.Count == 0)
        {
            VerifyRequired(null, gate);
        }
        else
        {
            foreach (ConditionSet set in working.ConditionSets)
            {
                VerifyRequired(set.ConditionGroup, gate);
            }
        }

        return working;
    }

    /// <summary>
    /// Scopes every condition set in a segment, and gives a segment with no sets one to be scoped.
    /// </summary>
    /// <remarks>
    /// Each set becomes its own subquery, so each takes the scope independently. Scoping one arm of
    /// an <c>Except</c> and not the other returns precisely the rows the scope exists to hide:
    /// <c>AllRows EXCEPT (AllRows WHERE TenantId = 5)</c> is every other tenant, by name.
    /// <para>
    /// A segment carrying no sets is treated by the pipeline as "return everything", and it builds
    /// itself a filter with a null condition group to do so. There is nothing for the scope to wrap,
    /// so one set is created to hold it — without which the emptiest possible segment would be the
    /// widest possible result.
    /// </para>
    /// </remarks>
    private static void InjectIntoSets<T>(Segment segment, Gate gate) where T : class
    {
        if (gate.TypePolicy.Forced.Count == 0)
        {
            return;
        }

        if (segment.ConditionSets is null || segment.ConditionSets.Count == 0)
        {
            ConditionGroup? scope = Inject<T>(null, gate);

            if (scope is not null)
            {
                segment.ConditionSets = new List<ConditionSet>
                {
                    new() { Sort = 0, ConditionGroup = scope }
                };
            }

            return;
        }

        foreach (ConditionSet set in segment.ConditionSets)
        {
            ConditionGroup? scoped = Inject<T>(set.ConditionGroup, gate);

            if (scoped is not null)
            {
                set.ConditionGroup = scoped;
            }
        }
    }

    /// <summary>
    /// Refuses any field the policy keeps out of a set operation entirely.
    /// </summary>
    private static void GateSegmentParticipation(Segment segment, Gate gate)
    {
        foreach (string field in SegmentFields(segment))
        {
            FieldPolicy policy = gate.PolicyFor(field, PolicyFeature.Segment);

            if (!policy.Allows(PolicyFeature.Segment))
            {
                gate.Deny(field, PolicyFeature.Segment, PolicyErrorCode.FieldDeniedForSegment, policy);
            }
        }
    }

    /// <summary>
    /// Refuses, in the strict tier only, a filter inside a set operation on a field the caller may
    /// not project.
    /// </summary>
    /// <remarks>
    /// Design section 7.1. <c>Segment</c> composes Union, Intersect and Except across subqueries,
    /// and where a field is deny-select but allow-where,
    /// <c>AllStaff EXCEPT (AllStaff WHERE Salary &gt; 100000)</c> returns exactly the people
    /// earning under 100k — by name, with the salary column never selected. The protected value is
    /// reconstructed from set membership, so narrowing a projection does not help: whether a row
    /// survives the Except already answers the question the projection was hiding.
    /// <para>
    /// Strict only, and deliberately so. This refuses a filter that is legitimate outside a set
    /// operation, and the convenience tier's caller is the project's own front end. Which posture
    /// pays that cost is a threat-model decision, not one the engine should make silently.
    /// </para>
    /// <para>
    /// Distinct from <see cref="GateSegmentParticipation"/>, which refuses a field denied for
    /// <see cref="PolicyFeature.Segment"/> outright. This one refuses a field nobody denied for
    /// segments at all, because of what a set operation can be made to disclose about it.
    /// </para>
    /// </remarks>
    private static void GateSegmentInference(Segment segment, Gate gate)
    {
        if (!gate.IsStrict || segment.ConditionSets is null)
        {
            return;
        }

        foreach (ConditionSet set in segment.ConditionSets)
        {
            // Every condition at every depth. A walk that stopped at the top level would be
            // bypassed by nesting one group deeper, which is the cheapest evasion there is.
            foreach (string field in GroupFields(set.ConditionGroup))
            {
                FieldPolicy policy = gate.PolicyFor(field, PolicyFeature.Select);

                if (policy.Allows(PolicyFeature.Select))
                {
                    continue;
                }

                gate.DenySegmentInference(field, policy);
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
            FieldPolicy policy = gate.PolicyFor(field, PolicyFeature.Order);

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
    /// A segment with no projection composes whole rows, so deny-select was enforced here against
    /// precisely the callers who volunteered one and bypassable by sending none — the hole the
    /// filter path closes by synthesizing, one surface along. Participation does not compensate:
    /// it examines the fields the caller named, and a field nobody named is a field nobody gated.
    /// <para>
    /// Synthesizing unconditionally, with no opt-out. The filter path skips it for a caller
    /// composing one clause at a time; a segment has no such form, and its only terminal
    /// materializes.
    /// </para>
    /// </remarks>
    private static void GateSegmentSelects(Segment segment, Gate gate)
    {
        if (segment.Selects is null || segment.Selects.Count == 0)
        {
            if (SynthesizedProjection(gate) is List<string> synthesized)
            {
                segment.Selects = synthesized;
            }

            return;
        }

        List<string> kept = GateProjection(segment.Selects, gate);

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
    /// Refuses a filter that spends more than the budget allows.
    /// </summary>
    /// <remarks>
    /// After <see cref="EnforceCaps(Filter, Gate)"/> rather than beside it, and for the opposite
    /// reason to the one that puts the structural caps first: this one has to resolve a policy for
    /// every field the caller named, which is exactly the unbounded work the structural caps exist
    /// to stop. They run first and bound how many fields this pass will ever look at.
    /// <para>
    /// Before gating, so a field is charged for being named rather than for surviving. Charging
    /// only what survives would let a caller measure the policy by watching the budget: a name that
    /// costs nothing is a name that was dropped.
    /// </para>
    /// <para>
    /// Selects and orders the caller wrote are charged; a projection this library synthesizes for a
    /// caller who named none is not, because charging for every field of a type would refuse a
    /// query nobody made expensive.
    /// </para>
    /// </remarks>
    private static void EnforceCost(Filter filter, Gate gate)
    {
        Budget budget = new();

        Spend(filter.ConditionGroup, gate, budget);

        if (filter.Orders is not null)
        {
            foreach (OrderBy order in filter.Orders)
            {
                budget.Charge(order.Field, gate);
            }
        }

        if (filter.Selects is not null)
        {
            foreach (string field in filter.Selects)
            {
                budget.Charge(field, gate);
            }
        }

        gate.CheckCost(budget.Total);
    }

    /// <summary>
    /// Refuses a summary that spends more than the budget allows.
    /// </summary>
    /// <remarks>
    /// <c>Having</c> and <c>Orders</c> name aggregate aliases and dot-stripped group-by keys rather
    /// than property paths, so they are not charged here — the fields they stand for are charged
    /// through <c>GroupBy</c>, and charging both would bill the caller twice for one column.
    /// </remarks>
    private static void EnforceCost(Summary summary, Gate gate)
    {
        Budget budget = new();

        Spend(summary.ConditionGroup, gate, budget);

        if (summary.GroupBy?.Fields is not null)
        {
            foreach (string field in summary.GroupBy.Fields)
            {
                budget.Charge(field, gate);
            }
        }

        if (summary.GroupBy?.AggregateBy is not null)
        {
            foreach (AggregateBy aggregate in summary.GroupBy.AggregateBy)
            {
                budget.Charge(aggregate.Field, gate);
            }
        }

        gate.CheckCost(budget.Total);
    }

    /// <summary>
    /// Refuses a set operation that spends more than the budget allows.
    /// </summary>
    /// <remarks>
    /// One budget across every set rather than one per set. A per-set budget would be spent as many
    /// times as the caller cares to add sets, which is the same unbounded work with more typing.
    /// </remarks>
    private static void EnforceCost(Segment segment, Gate gate)
    {
        Budget budget = new();

        foreach (string field in SegmentFields(segment))
        {
            budget.Charge(field, gate);
        }

        gate.CheckCost(budget.Total);
    }

    /// <summary>Charges every condition in a group tree.</summary>
    private static void Spend(ConditionGroup? group, Gate gate, Budget budget)
    {
        if (group is null)
        {
            return;
        }

        if (group.Conditions is not null)
        {
            foreach (Condition condition in group.Conditions)
            {
                budget.Charge(condition.Field, budget: gate);
            }
        }

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in group.SubConditionGroups)
            {
                Spend(sub, gate, budget);
            }
        }
    }

    /// <summary>
    /// What one query has spent so far.
    /// </summary>
    /// <remarks>
    /// A running total rather than a sum over a collected list, because the list would be the same
    /// unbounded thing the budget exists to bound.
    /// </remarks>
    private sealed class Budget
    {
        /// <summary>What has been charged so far.</summary>
        internal long Total { get; private set; }

        /// <summary>
        /// Charges one reference to a field.
        /// </summary>
        /// <param name="fieldPath">The field, canonical by the time this runs.</param>
        /// <param name="budget">The gate, which knows the weights and the standing default.</param>
        internal void Charge(string? fieldPath, Gate budget)
        {
            if (string.IsNullOrWhiteSpace(fieldPath))
            {
                return;
            }

            // A long, because the total is a product of two caller-controlled numbers: the number of
            // references the structural caps allow and the weight an operator set. Both are ints,
            // and int arithmetic would wrap a large enough product back into a total under budget —
            // which is a refusal turning into a grant on overflow.
            Total += budget.CostOf(fieldPath!);
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
                FieldPolicy policy = gate.PolicyFor(field, PolicyFeature.Where);

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
            FieldPolicy policy = gate.PolicyFor(field, PolicyFeature.Order);

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

        List<string> kept = GateProjection(filter.Selects, gate);

        if (kept.Count == 0)
        {
            throw gate.Exception(WholeClause, PolicyFeature.Select, PolicyErrorCode.AllSelectsDenied, null);
        }

        filter.Selects = kept;
    }

    /// <summary>
    /// Gates a projection the caller wrote, against what that projection actually carries.
    /// </summary>
    /// <remarks>
    /// A projection carries two things its text does not say. Naming a navigation names every
    /// field beneath it, because the pipeline projects a navigation whole; and every nested node
    /// carries its own key, because the projection builder adds one whether it was asked for or
    /// not. Resolving the text alone therefore enforces deny-select against whoever spelled a path
    /// out in full and leaves it bypassable by naming the parent, or by naming a sibling of the
    /// key.
    /// <para>
    /// A navigation with nothing denied beneath it is left exactly as the caller wrote it. The
    /// expansion happens only where there is something to narrow, so a type the policy has no
    /// opinion about still generates the SQL the unguarded path would.
    /// </para>
    /// </remarks>
    private static List<string> GateProjection(IEnumerable<string> selects, Gate gate)
    {
        List<string> kept = new();

        void Keep(string path)
        {
            if (!kept.Contains(path, StringComparer.Ordinal))
            {
                kept.Add(path);
            }
        }

        bool Allowed(string path)
        {
            FieldPolicy policy = gate.PolicyFor(path, PolicyFeature.Select);

            return policy.Allows(PolicyFeature.Select)
                || !gate.Refuse(path, PolicyFeature.Select, PolicyErrorCode.FieldDeniedForSelect, policy);
        }

        foreach (string field in selects)
        {
            // The field's own decision first. A navigation that is itself denied is refused as it
            // stands; what it carries is only asked about once it has survived on its own account.
            if (!Allowed(field))
            {
                continue;
            }

            if (!gate.IsNavigation(field))
            {
                Keep(field);
                KeepCarriedKeys(field, gate, Keep);

                continue;
            }

            IReadOnlyList<string> carried = gate.ProjectionUnder(field);
            List<string> survivors = new(carried.Count);
            bool narrowed = false;

            foreach (string path in carried)
            {
                if (Allowed(path))
                {
                    survivors.Add(path);
                }
                else
                {
                    narrowed = true;
                }
            }

            if (!narrowed)
            {
                Keep(field);

                continue;
            }

            foreach (string path in survivors)
            {
                Keep(path);
            }
        }

        return kept;
    }

    /// <summary>
    /// Gates the key of every nested node a path passes through, and keeps the ones that survive.
    /// </summary>
    /// <remarks>
    /// The projection builder adds the key of each nested node whether the caller named it or not,
    /// so the key is in the result whatever this decides. Naming it here is what lets the outbound
    /// transform see it as carried — without that a masked nested key leaves unmasked, because the
    /// walker matches a carried path against the projection and the caller never wrote this one.
    /// <para>
    /// A key that is denied outright refuses the clause rather than dropping it. Dropping is not
    /// an option this layer has: the projection is built with the key regardless, so the only way
    /// to honour the denial is to decline to build it.
    /// </para>
    /// </remarks>
    private static void KeepCarriedKeys(string path, Gate gate, Action<string> keep)
    {
        int cut = path.IndexOf('.', StringComparison.Ordinal);

        while (cut >= 0)
        {
            string node = path[..cut];

            if (gate.HasKey(node))
            {
                string key = node + ".Id";
                FieldPolicy policy = gate.PolicyFor(key, PolicyFeature.Select);

                if (policy.Allows(PolicyFeature.Select))
                {
                    keep(key);
                }
                else
                {
                    gate.Deny(key, PolicyFeature.Select, PolicyErrorCode.FieldDeniedForSelect, policy);
                }
            }

            cut = path.IndexOf('.', cut + 1);
        }
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
        if (SynthesizedProjection(gate) is List<string> allowed)
        {
            filter.Selects = allowed;
        }
    }

    /// <summary>
    /// The allowed projection to stand in for a missing one, or null to leave it missing.
    /// </summary>
    /// <remarks>
    /// Shared by the filter and the segment paths, because they are closing the same hole and a
    /// second copy of this is a second place to forget.
    /// </remarks>
    private static List<string>? SynthesizedProjection(Gate gate)
    {
        List<string> allowed = new();
        bool anyDenied = false;

        foreach (string field in gate.ProjectableFields())
        {
            FieldPolicy policy = gate.PolicyFor(field, PolicyFeature.None);

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
            return null;
        }

        if (allowed.Count == 0)
        {
            throw gate.Exception(WholeClause, PolicyFeature.Select, PolicyErrorCode.AllSelectsDenied, null);
        }

        return allowed;
    }

    /// <summary>
    /// Refuses a request that fails to supply a filter the policy requires.
    /// </summary>
    /// <remarks>
    /// Naming the required field is not the test. A caller sending
    /// <c>Status = A OR TenantId = 5</c> has named it and still receives every other tenant's rows
    /// matching <c>Status = A</c> — a requirement met on paper and defeated in fact, which is worse
    /// than no requirement because the attribute in the source reads as though it is in force.
    /// <para>
    /// A condition counts only when it is <em>conjunctively binding</em>: the path from the root
    /// down to the group holding it must be unbroken <c>And</c>, and its operator must be one the
    /// requirement accepts. Both halves matter — an <c>And</c> group nested inside an <c>Or</c> is
    /// not binding, and <c>TenantId != 5</c> inside a perfectly good <c>And</c> is every tenant but
    /// one.
    /// </para>
    /// <para>
    /// Throws in both tiers. Spec section 2.5 puts a missing requirement in the throw-in-both-tiers
    /// row for the same reason a blocked <c>WHERE</c> is there: there is no lenient way to drop a
    /// scope, only a catastrophic one.
    /// </para>
    /// </remarks>
    private static void VerifyRequired(ConditionGroup? group, Gate gate)
    {
        if (gate.TypePolicy.Required.Count == 0)
        {
            return;
        }

        Dictionary<string, List<Operator>> binding = new(StringComparer.OrdinalIgnoreCase);

        CollectBinding(group, ancestorsBind: true, binding);

        foreach (KeyValuePair<string, IReadOnlyList<Operator>> requirement in gate.TypePolicy.Required)
        {
            if (binding.TryGetValue(requirement.Key, out List<Operator>? used)
                && used.Exists(op => requirement.Value.Contains(op)))
            {
                continue;
            }

            gate.RequireMissing(requirement.Key);
        }
    }

    /// <summary>
    /// Collects the operators used against each field by a condition that actually narrows the
    /// result.
    /// </summary>
    /// <param name="group">The group being walked.</param>
    /// <param name="ancestorsBind">False once any ancestor group has been found to disjoin.</param>
    /// <param name="binding">The accumulator.</param>
    private static void CollectBinding(
        ConditionGroup? group, bool ancestorsBind, Dictionary<string, List<Operator>> binding)
    {
        if (group is null)
        {
            return;
        }

        bool binds = ancestorsBind && Conjoins(group);

        if (binds && group.Conditions is not null)
        {
            foreach (Condition condition in group.Conditions)
            {
                if (string.IsNullOrWhiteSpace(condition.Field))
                {
                    continue;
                }

                if (!binding.TryGetValue(condition.Field!, out List<Operator>? operators))
                {
                    operators = new List<Operator>();
                    binding[condition.Field!] = operators;
                }

                operators.Add(condition.Operator);
            }
        }

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in group.SubConditionGroups)
            {
                CollectBinding(sub, binds, binding);
            }
        }
    }

    /// <summary>
    /// True when every child of a group must hold for the group to hold.
    /// </summary>
    /// <remarks>
    /// A group with a single child conjoins whatever its connector says, because there is nothing to
    /// disjoin it with — the pipeline emits that child alone. Treating it as a disjunction would
    /// refuse a filter that genuinely does narrow the result and push callers into rewriting sound
    /// filters to please the checker.
    /// <para>
    /// Children are counted without asking whether each one contributes a predicate. An empty
    /// subgroup emits nothing, so counting it makes this stricter than it strictly needs to be,
    /// which is the direction to be wrong in.
    /// </para>
    /// </remarks>
    private static bool Conjoins(ConditionGroup group) =>
        group.Connector == Connector.And
        || (group.Conditions?.Count ?? 0) + (group.SubConditionGroups?.Count ?? 0) <= 1;

    /// <summary>
    /// Wraps a caller's condition group in a new root that also carries every forced predicate.
    /// </summary>
    /// <remarks>
    /// The shape is a correctness requirement and not an implementation detail. Given a caller
    /// group of <c>(Status = A OR Status = B)</c>, appending the tenant term <em>inside</em> that
    /// group produces <c>(Status = A OR Status = B OR TenantId = 5)</c> — an OR that returns every
    /// tenant's rows matching A or B. The forced term must always sit at a new root, joined by
    /// <c>And</c>, whatever the caller's own group does.
    /// <para>
    /// A caller who sent no group at all still gets one. That caller is precisely who a forced scope
    /// exists for: a null group reaches the pipeline as "no where clause", and leaving it null hands
    /// back every row of the table.
    /// </para>
    /// <para>
    /// Nothing is injected in a dry run. Dry run promises the same data the unguarded path would
    /// return, and a predicate that narrows the result would make a canary understate its own blast
    /// radius. The decision is still recorded, which is the whole point of the canary.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="callers">The caller's group, already gated. May be null.</param>
    /// <param name="gate">The per-query state.</param>
    /// <returns>The group to hand the pipeline.</returns>
    /// <exception cref="PolicyException">
    /// Thrown when a predicate reads an ambient value the caller's context does not supply.
    /// </exception>
    private static ConditionGroup? Inject<T>(ConditionGroup? callers, Gate gate) where T : class
    {
        if (gate.TypePolicy.Forced.Count == 0)
        {
            return callers;
        }

        List<Condition> injected = new(gate.TypePolicy.Forced.Count);

        foreach (ForcedPredicate predicate in gate.TypePolicy.Forced)
        {
            Condition? condition = gate.Materialize<T>(predicate, injected.Count);

            if (condition is not null)
            {
                injected.Add(condition);
            }
        }

        if (injected.Count == 0)
        {
            return callers;
        }

        ConditionGroup root = new()
        {
            Sort = 0,
            Connector = Connector.And,
            Conditions = injected,
            SubConditionGroups = new List<ConditionGroup>()
        };

        if (callers is not null)
        {
            callers.Sort = 0;

            root.SubConditionGroups.Add(callers);
        }

        return root;
    }

    /// <summary>
    /// Turns a name the caller wrote into the canonical field path it stands for, resolving an
    /// alias where there is one.
    /// </summary>
    /// <remarks>
    /// The single entry point for every name a caller supplies, in every clause. Nothing else may
    /// rewrite one: the defect this feature keeps producing is two spellings normalized by two
    /// routines, where a policy fragment then fails to match and a field that fails to match is a
    /// field left allowed.
    /// <para>
    /// A name can mean an alias, a property path, or both. Both is not a case with a safe answer —
    /// preferring the property ignores a rule the caller was granted, preferring the alias sends the
    /// filter to a different column — so it is refused rather than resolved. The same applies when
    /// two aliases collide, and when one aliased type is reachable by two navigations.
    /// </para>
    /// <para>
    /// A name matching nothing is handed to <c>Validate&lt;T&gt;()</c> unchanged, so it fails with
    /// the error an unguarded query would give. A caller therefore cannot probe for which fields
    /// exist by watching how the policy layer refuses them.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="name">The name as the caller wrote it.</param>
    /// <param name="gate">The per-query state, carrying the type's alias map.</param>
    /// <returns>The canonical field path.</returns>
    /// <exception cref="PolicyException">Thrown when the name could mean more than one field.</exception>
    /// <exception cref="LogicException">Thrown when the name names nothing.</exception>
    private static string ResolveName<T>(string name, Gate gate) where T : class
    {
        // A type nobody has aliased takes the path it always did, with no extra reflection and no
        // behavioural difference from before this existed.
        if (gate.TypePolicy.Aliases.Count == 0)
        {
            return name.Validate<T>();
        }

        string spoken = name.Trim();

        List<string> candidates = new();

        if (gate.TypePolicy.Aliases.TryGetValue(spoken, out IReadOnlyList<string>? aliased))
        {
            foreach (string path in aliased)
            {
                // Canonicalized here rather than at resolution, because the alias target is a path
                // on T and only the pipeline's own routine can say what its canonical spelling is.
                Add(candidates, path.Validate<T>());
            }
        }

        // Probed, not assumed. An alias adds a spelling and never removes one, so a name that is
        // also a real property path still means that property — and if it means both, that is the
        // collision this refuses.
        string? asPath = TryValidate<T>(spoken);

        if (asPath is not null)
        {
            Add(candidates, asPath);
        }

        if (candidates.Count > 1 && OneMemberReachedManyWays(candidates) is { } declared)
        {
            // One declaration, several paths to it — not two declarations competing. A type that
            // appears inside its own navigation graph yields EmployeeCode, Manager.EmployeeCode and
            // Subordinates.EmployeeCode from the single [DwAlias] on the root member, and refusing
            // that made the alias unusable on any entity with a bidirectional navigation, which is
            // most of them. The root is the declaration site and the only path the author named, so
            // it is what the alias means.
            //
            // Narrow on purpose. Two different members sharing one alias still collide, because
            // there the refusal is right: preferring either one silently discards a spelling
            // somebody wrote.
            candidates = new List<string> { declared };
        }

        if (candidates.Count > 1)
        {
            throw gate.Exception(spoken, PolicyFeature.None, PolicyErrorCode.AmbiguousFieldName, null);
        }

        if (candidates.Count == 0)
        {
            // Nothing matched. Hand it back to the validator so the failure is the ordinary one.
            return name.Validate<T>();
        }

        string canonical = candidates[0];

        gate.RecordSpelling(canonical, spoken);

        return canonical;
    }

    /// <summary>
    /// The root path when every candidate is the same member reached through navigations, otherwise
    /// null.
    /// </summary>
    /// <remarks>
    /// True only when exactly one candidate is a bare root path and every other candidate is that
    /// same path behind a navigation prefix. <c>EmployeeCode</c> with <c>Manager.EmployeeCode</c>
    /// qualifies; <c>EmployeeCode</c> with <c>Address.PostCode</c> does not, and stays ambiguous.
    /// </remarks>
    /// <param name="candidates">The paths a spoken name matched.</param>
    /// <returns>The root path, or null when the candidates are genuinely different members.</returns>
    private static string? OneMemberReachedManyWays(List<string> candidates)
    {
        string? root = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].IndexOf('.') >= 0)
            {
                continue;
            }

            if (root is not null)
            {
                // Two bare roots cannot both be the declaration site.
                return null;
            }

            root = candidates[i];
        }

        if (root is null)
        {
            return null;
        }

        string suffix = "." + root;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (!ReferenceEquals(candidates[i], root)
                && !candidates[i].EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return root;
    }

    /// <summary>Adds a path to the candidate set, ignoring one already present in another casing.</summary>
    private static void Add(List<string> candidates, string path)
    {
        if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(path);
        }
    }

    /// <summary>
    /// Returns the canonical form of a name when it is a real property path, otherwise null.
    /// </summary>
    /// <remarks>
    /// Control flow through an exception, and deliberately so. The alternative is a second copy of
    /// the reflection walk that decides what a valid path is — and a second copy is exactly how the
    /// two spellings drift apart, which is the failure mode this whole routine exists to prevent.
    /// The call is cached by <c>CacheReflection</c>, so the cost is a dictionary lookup on the
    /// path that succeeds and a throw only on a name that is an alias rather than a path.
    /// </remarks>
    private static string? TryValidate<T>(string name) where T : class
    {
        try
        {
            return name.Validate<T>();
        }
        catch (LogicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rewrites every field path on the clone into the canonical form the pipeline uses.
    /// </summary>
    private static void Canonicalize<T>(Filter filter, Gate gate) where T : class
    {
        if (filter.ConditionGroup is not null)
        {
            CanonicalizeGroup<T>(filter.ConditionGroup, gate);
        }

        if (filter.Selects is not null)
        {
            for (int i = 0; i < filter.Selects.Count; i++)
            {
                filter.Selects[i] = ResolveName<T>(filter.Selects[i], gate);
            }
        }

        if (filter.Orders is not null)
        {
            foreach (OrderBy order in filter.Orders)
            {
                CanonicalizeOrder<T>(order, gate);
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
    private static void CanonicalizeGroup<T>(ConditionGroup group, Gate gate) where T : class
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

                condition.Field = ResolveName<T>(condition.Field!, gate);
            }
        }

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in group.SubConditionGroups)
            {
                CanonicalizeGroup<T>(sub, gate);
            }
        }
    }

    /// <summary>
    /// Canonicalizes one order clause.
    /// </summary>
    private static void CanonicalizeOrder<T>(OrderBy order, Gate gate) where T : class
    {
        if (string.IsNullOrWhiteSpace(order.Field))
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        order.Field = ResolveName<T>(order.Field!, gate);
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

        // The name the caller actually wrote for a canonical path, when the two differ. Only a
        // refusal reads this: the trace records the path the policy governs, so it can be read
        // straight against the policy, while the exception crosses the boundary and must not carry
        // an internal column name back to a caller who never used one.
        private readonly Dictionary<string, string> _spoken = new(StringComparer.Ordinal);

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
            TypePolicy = resolver.ResolveType(entityType, context);
        }

        /// <summary>
        /// What this type's policy says that no single field can answer: the names this caller may
        /// use, the predicates to inject, and the fields the caller must filter on.
        /// </summary>
        internal TypePolicy TypePolicy { get; }

        /// <summary>The caller this query is being sanitized for.</summary>
        internal DwPolicyContext Context => _context;

        /// <summary>The enforcement posture in force.</summary>
        internal DwPolicyOptions Options => _options;

        /// <summary>
        /// Remembers that the caller reached a canonical path by writing some other name.
        /// </summary>
        internal void RecordSpelling(string canonicalPath, string spoken)
        {
            if (!string.Equals(canonicalPath, spoken, StringComparison.Ordinal))
            {
                _spoken[canonicalPath] = spoken;
            }
        }

        /// <summary>
        /// The name the caller wrote for a path, or the path itself when they wrote that.
        /// </summary>
        internal string Spoken(string fieldPath) =>
            _spoken.TryGetValue(fieldPath, out string? spoken) ? spoken : fieldPath;

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

        /// <summary>
        /// True when a validated path names a navigation rather than a scalar.
        /// </summary>
        internal bool IsNavigation(string path)
        {
            Type? type = TypeAt(path);

            return type is not null && !CacheReflection.IsSimpleType(type);
        }

        /// <summary>
        /// True when the type a path names carries a key the projection builder will add.
        /// </summary>
        internal bool HasKey(string path)
        {
            Type? type = TypeAt(path);

            return type is not null && CacheReflection.FindProperty(type, "Id") is not null;
        }

        /// <summary>
        /// Every path a projection actually carries when the caller names one navigation.
        /// </summary>
        /// <remarks>
        /// Naming a navigation projects the whole object, so the caller who named the parent
        /// receives every field beneath it. Resolving the name alone therefore enforces
        /// deny-select against whoever spelled the child out in full and leaves it bypassable by
        /// asking for its parent instead — the same shape as sending no projection at all.
        /// <para>
        /// The walk stops where <see cref="AttributePolicyProvider"/>'s does, and carries the same
        /// cycle guard. Nothing below that depth holds a fragment, so there is no decision down
        /// there to enforce and descending further would only enumerate paths the resolver cannot
        /// speak about.
        /// </para>
        /// </remarks>
        internal IReadOnlyList<string> ProjectionUnder(string path)
        {
            List<string> paths = new();
            Type? type = TypeAt(path);

            if (type is not null)
            {
                Descend(path, type, 1, paths, new HashSet<Type>());
            }

            return paths;
        }

        private static void Descend(
            string prefix, Type type, int depth, List<string> paths, HashSet<Type> seen)
        {
            // A type reachable from itself would otherwise enumerate until the stack ran out.
            if (!seen.Add(type))
            {
                return;
            }

            foreach (KeyValuePair<string, PropertyInfo> entry in CacheReflection.GetTypeProperties(type))
            {
                PropertyInfo property = entry.Value;

                if (!property.CanRead
                    || !property.CanWrite
                    || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                string child = $"{prefix}.{property.Name}";

                if (CacheReflection.IsSimpleType(property.PropertyType))
                {
                    paths.Add(child);

                    continue;
                }

                Type nested = CacheReflection.GetCollectionElementType(property.PropertyType)
                    ?? property.PropertyType;

                // A collection of a primitive is a value the projection assigns whole, not a
                // navigation into its element type. Descending into byte would enumerate nothing
                // and drop the member from an expansion that is supposed to preserve it.
                if (CacheReflection.IsSimpleType(nested))
                {
                    paths.Add(child);

                    continue;
                }

                if (depth >= AttributePolicyProvider.MaxDepth)
                {
                    continue;
                }

                Descend(child, nested, depth + 1, paths, seen);
            }

            seen.Remove(type);
        }

        /// <summary>The type a validated path names, or null when it names nothing.</summary>
        private Type? TypeAt(string path)
        {
            try
            {
                return CacheReflection.GetFieldType(_entityType, path);
            }
            catch (LogicException)
            {
                return null;
            }
        }

        /// <summary>
        /// What one reference to a field spends.
        /// </summary>
        /// <remarks>
        /// The field's own weight when something weighs it, and the standing default otherwise.
        /// Null and zero are different answers here: null is "nobody weighed this field", which the
        /// host's default decides, and zero is "this field is free", which the host's default must
        /// not override.
        /// </remarks>
        internal int CostOf(string fieldPath) =>
            PolicyFor(fieldPath, PolicyFeature.None).CostWeight ?? _options.Caps.DefaultFieldCost;

        /// <summary>Refuses a query that spends more than the budget allows.</summary>
        /// <remarks>
        /// Its own error code rather than <c>CapExceeded</c>. A filter well inside every structural
        /// limit can still name one expensive field enough times to generate work nothing else
        /// bounds, and an operator reading a log has to know which cap to raise.
        /// </remarks>
        internal void CheckCost(long total)
        {
            if (total <= _options.Caps.MaxQueryCost)
            {
                return;
            }

            string origin =
                $"MaxQueryCost cap ({_options.Caps.MaxQueryCost}), request cost {total}";

            _trace.Add(new PolicyDecision(
                WholeClause, PolicyFeature.None, PolicyAction.Denied, origin));

            if (IsDryRun)
            {
                return;
            }

            throw new PolicyException(
                PolicyErrorCode.QueryCostExceeded, WholeClause, PolicyFeature.None, _options.Tier)
            {
                SourceOrigin = origin
            };
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

        /// <summary>
        /// Turns a forced predicate into the condition that will be injected, or null when the dry
        /// run means nothing is to be injected at all.
        /// </summary>
        /// <typeparam name="T">The entity type being queried.</typeparam>
        /// <param name="predicate">The predicate to materialize.</param>
        /// <param name="sort">
        /// Its position among the injected conditions. <c>ConditionGroup.Validate</c> refuses
        /// duplicate sort values, so a second forced predicate on a type would otherwise turn every
        /// query on it into a validation failure.
        /// </param>
        /// <returns>The condition to inject, or null.</returns>
        /// <exception cref="PolicyException">
        /// Thrown when the predicate reads an ambient value the caller's context does not supply.
        /// </exception>
        /// <exception cref="LogicException">
        /// Thrown when the predicate names a field that does not exist on <typeparamref name="T"/>.
        /// That is a misconfiguration rather than a policy decision, and it fails loudly.
        /// </exception>
        internal Condition? Materialize<T>(ForcedPredicate predicate, int sort) where T : class
        {
            // Validated against T because a predicate from a runtime rule carries whatever field
            // path the rule was written with, and the pipeline only accepts canonical ones.
            string path = predicate.FieldPath.Validate<T>();

            List<object> values = new();

            if (predicate.ReadsContext)
            {
                // Present-but-null counts as missing. That is the shape a forgotten claim actually
                // takes, and filtering by null is not the scope anybody meant. Spec section 4.3: a
                // tenant scope that silently fails to apply is worse than a failed request.
                if (!_context.TryGetValue(predicate.ContextValue!, out object? value) || value is null)
                {
                    string origin =
                        $"forced predicate on '{path}' reads the ambient value " +
                        $"'{predicate.ContextValue}', which the context does not supply";

                    _trace.Add(new PolicyDecision(
                        path, PolicyFeature.Where, PolicyAction.Denied, origin));

                    // Dry run overrides the whole table, this row included. An operator running a
                    // canary needs to be told the scope would not have resolved, and the trace above
                    // is how they are told.
                    if (IsDryRun)
                    {
                        return null;
                    }

                    throw new PolicyException(
                        PolicyErrorCode.MissingContextValue, path, PolicyFeature.Where, _options.Tier)
                    {
                        SourceOrigin = origin
                    };
                }

                values.Add(value);
            }
            else if (!predicate.IsNullCheck)
            {
                values.Add(predicate.Value!);
            }

            RecordInjected(path, predicate.Operator);

            if (IsDryRun)
            {
                return null;
            }

            return new Condition
            {
                Sort = sort,
                Field = path,
                DataType = predicate.DataType,
                Operator = predicate.Operator,
                Values = values
            };
        }

        /// <summary>
        /// Refuses a request that did not supply a filter the policy requires.
        /// </summary>
        /// <remarks>
        /// Reported under the field's public name where it has one. This refusal is unlike a denial:
        /// the caller has to act on it, and naming the field in a vocabulary they are allowed to use
        /// is the difference between a fixable error and a riddle. The trace keeps the canonical
        /// path, as it does everywhere else.
        /// </remarks>
        internal void RequireMissing(string fieldPath)
        {
            string named = PolicyFor(fieldPath, PolicyFeature.None).Alias ?? fieldPath;

            string origin =
                $"required filter on '{named}' was not supplied by a condition that narrows the result";

            _trace.Add(new PolicyDecision(fieldPath, PolicyFeature.Where, PolicyAction.Denied, origin));

            if (IsDryRun)
            {
                return;
            }

            throw new PolicyException(
                PolicyErrorCode.RequiredFilterMissing, named, PolicyFeature.Where, _options.Tier)
            {
                SourceOrigin = origin
            };
        }

        /// <summary>
        /// Refuses a filter inside a set operation on a field the caller may not project.
        /// </summary>
        /// <remarks>
        /// Carries its own reason rather than going through <see cref="Deny"/>, because the refusal
        /// is not what the field's policy appears to say: the field is allowed for filtering, and
        /// the same filter succeeds outside a segment. Without the origin an operator reading a
        /// trace would go looking for a denial that is not there.
        /// </remarks>
        internal void DenySegmentInference(string fieldPath, FieldPolicy policy)
        {
            const string origin =
                "denied for projection, and a set operation reconstructs a field from membership " +
                "rather than from the columns it returns";

            Record(fieldPath, PolicyFeature.Segment, PolicyAction.Denied, policy);

            if (IsDryRun)
            {
                return;
            }

            throw new PolicyException(
                PolicyErrorCode.FieldDeniedForSegment, Spoken(fieldPath), PolicyFeature.Segment,
                _options.Tier)
            {
                SourceOrigin = origin
            };
        }

        /// <summary>Records that the library added a predicate the caller did not send.</summary>
        internal void RecordInjected(string fieldPath, Operator op) =>
            _trace.Add(new PolicyDecision(
                fieldPath, PolicyFeature.Where, PolicyAction.Injected, $"forced predicate ({op})"));

        /// <summary>Resolves one field's policy, once per query.</summary>
        internal FieldPolicy PolicyFor(string fieldPath, PolicyFeature feature)
        {
            if (!_resolved.TryGetValue(fieldPath, out FieldPolicy? policy))
            {
                policy = _resolver.Resolve(_entityType, fieldPath, _context);
                _resolved[fieldPath] = policy;
            }

            // Outside the memo on purpose. The policy is resolved once per field per query, but an
            // audit records uses, and a field named in a condition and again in an order was used
            // twice.
            if (feature != PolicyFeature.None && policy.IsAudited(feature))
            {
                Audit(fieldPath, feature, policy);
            }

            return policy;
        }

        /// <summary>
        /// Records one use of an audited field on the caller's context, refusing the query when
        /// there is no room left to record it.
        /// </summary>
        /// <remarks>
        /// Fail closed. Dropping the record instead would leave the access happening with nothing
        /// written down, which is the single outcome <c>[DwAudit]</c> exists to make impossible —
        /// and it would leave no trace of having dropped anything either.
        /// <para>
        /// A dry run still records. It changes what the policy does, not what it saw, and a canary
        /// rollout with no evidence of what it was about to refuse is one nobody can evaluate.
        /// </para>
        /// </remarks>
        private void Audit(string fieldPath, PolicyFeature feature, FieldPolicy policy)
        {
            DwAuditEvent recorded = new(
                DateTimeOffset.UtcNow,
                _entityType.FullName ?? _entityType.Name,
                fieldPath,
                feature,
                policy.EffectFor(feature),
                _context.Subjects,
                _context.Purpose,
                _options.Tier,
                IsDryRun);

            if (_context.TryRecordAudit(recorded, _options.Caps.MaxAuditEvents))
            {
                return;
            }

            string origin =
                $"MaxAuditEvents cap ({_options.Caps.MaxAuditEvents}) reached with the buffer "
                + "undrained";

            _trace.Add(new PolicyDecision(fieldPath, feature, PolicyAction.Denied, origin));

            throw new PolicyException(
                PolicyErrorCode.CapExceeded, fieldPath, feature, _options.Tier)
            {
                SourceOrigin = origin
            };
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

            // An explicit via wins: it names an aggregate alias or a grouping key, which is more
            // specific than the field alias this would otherwise fall back to.
            via ??= Spoken(fieldPath);

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

            // Reported under the name the caller used. Handing back the canonical path for a field
            // they only ever named by alias turns every refusal into schema disclosure, which is one
            // of the two reasons aliases exist. The trace keeps the canonical path.
            return new PolicyException(code, Spoken(fieldPath), feature, _options.Tier)
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
