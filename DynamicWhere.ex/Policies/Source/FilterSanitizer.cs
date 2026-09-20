using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Attributes;
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
    /// <param name="applyDefaultOrder">
    /// When true and the caller sent no orders, the type's declared default order is added, less every
    /// field this caller may not order by. False for a clause composed on its own, which orders
    /// nothing the caller did not ask it to.
    /// </param>
    /// <param name="rows">
    /// What the query's source puts in the members of its rows, which decides what a synthesized
    /// projection keeps. Null for an entity query whose model is unknown.
    /// </param>
    /// <returns>A sanitized copy, safe to hand to the existing pipeline.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="LogicException">
    /// Thrown under the convenience tier, or in a dry run, when a field path names nothing on
    /// <typeparamref name="T"/>. A field that does not exist has no policy, so it fails as validation
    /// before any policy decision is reached, with the error an unguarded query would give. Under the
    /// strict tier it is refused with the same code as a denied field instead, so the refusal does not
    /// tell a caller whether the field exists.
    /// </exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    internal static Filter Sanitize<T>(
        Filter filter,
        PolicyResolver resolver,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace,
        bool synthesizeProjection = true,
        bool applyDefaultOrder = true,
        RowShape? rows = null)
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
        Gate gate = new(typeof(T), resolver, context, options, trace) { Rows = rows ?? RowShape.Unknown };

        // Before any name is resolved. Counting needs no name, and resolving every name of an
        // oversized request is exactly the work the caps exist to refuse.
        EnforceCaps(working, gate);

        Canonicalize<T>(working, gate);

        EnforceNavigationDepth(working, gate);

        // After the caps, so a page the caller did write is still refused when it is too large,
        // and a page the caller did not write is bounded rather than unbounded.
        working.Page = DefaultPage(working.Page, options);

        long cost = Cost(working, gate);

        if (!gate.HidesExistence)
        {
            gate.CheckCost(cost);
        }

        GateConditions(working.ConditionGroup, gate);

        // Read before gating: a caller whose every order was dropped still sent orders, and gets
        // what they asked for rather than a default in their place.
        bool callerOrdered = working.Orders is { Count: > 0 };

        GateOrders(working, gate);

        if (applyDefaultOrder && !callerOrdered && DefaultOrders<T>(gate) is { Count: > 0 } defaults)
        {
            working.Orders = defaults;
        }

        GateSelects(working, gate, synthesizeProjection, rows ?? RowShape.Unknown);

        // The strict tier checks the bill once every field has passed its gate. It drops nothing, so a
        // request still here names no field the caller may not use, and a weighted field the caller may
        // not use has already been refused as a denied one, before its weight could set it apart from a
        // name that matches nothing.
        if (gate.HidesExistence)
        {
            gate.CheckCost(cost);
        }

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
    /// <param name="rows">What the source produces, when it can be read.</param>
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
        PolicyTrace trace,
        RowShape? rows = null)
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

        Gate gate = new(typeof(T), resolver, context, options, trace) { Rows = rows ?? RowShape.Unknown };

        EnforceCaps(working, gate);

        if (working.ConditionGroup is not null)
        {
            CanonicalizeGroup<T>(working.ConditionGroup, gate);
        }

        CanonicalizeGrouping<T>(working.GroupBy, gate);

        EnforceNavigationDepth(working, gate);

        // After the caps, so a page the caller did write is still refused when it is too large,
        // and a page the caller did not write is bounded rather than unbounded.
        working.Page = DefaultPage(working.Page, options);

        long cost = Cost(working, gate);

        if (!gate.HidesExistence)
        {
            gate.CheckCost(cost);
        }

        GateConditions(working.ConditionGroup, gate);
        GateGrouping(working.GroupBy, gate);

        // Built after grouping is canonical, because the references below stand for the canonical
        // paths and nothing else would map back to them.
        Dictionary<string, List<string>> references = BuildReferences(working.GroupBy, gate);

        GateHaving(working.Having, references, gate);
        GateSummaryOrders(working, references, gate);

        // The strict tier checks the bill once every field has passed its gate. It drops nothing, so a
        // request still here names no field the caller may not use, and a weighted field the caller may
        // not use has already been refused as a denied one, before its weight could set it apart from a
        // name that matches nothing.
        if (gate.HidesExistence)
        {
            gate.CheckCost(cost);
        }

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
    /// <param name="applyDefaultOrder">
    /// When true and the caller sent no orders, the type's declared default order is added, less every
    /// field this caller may not order by.
    /// </param>
    /// <param name="rows">
    /// What the query's source puts in the members of its rows, which decides what a synthesized
    /// projection keeps. Null for an entity query whose model is unknown.
    /// </param>
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
        PolicyTrace trace,
        bool applyDefaultOrder = true,
        RowShape? rows = null)
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

        Gate gate = new(typeof(T), resolver, context, options, trace)
        {
            InSegment = true,
            Rows = rows ?? RowShape.Unknown
        };

        EnforceCaps(working, gate);

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
                working.Selects[i] = ResolveName<T>(working.Selects[i], gate, computed: false);
            }
        }

        if (working.Orders is not null)
        {
            foreach (OrderBy order in working.Orders)
            {
                CanonicalizeOrder<T>(order, gate);
            }
        }

        EnforceNavigationDepth(working, gate);

        // After the caps, so a page the caller did write is still refused when it is too large,
        // and a page the caller did not write is bounded rather than unbounded.
        working.Page = DefaultPage(working.Page, options);

        long cost = Cost(working, gate);

        if (!gate.HidesExistence)
        {
            gate.CheckCost(cost);
        }

        GateSegmentParticipation(working, gate);
        GateSegmentInference(working, gate);

        if (working.ConditionSets is not null)
        {
            foreach (ConditionSet set in working.ConditionSets)
            {
                GateConditions(set.ConditionGroup, gate);
            }
        }

        bool callerOrdered = working.Orders is { Count: > 0 };

        GateSegmentOrders(working, gate);

        if (applyDefaultOrder && !callerOrdered && DefaultOrders<T>(gate, segment: true) is { Count: > 0 } defaults)
        {
            working.Orders = defaults;
        }

        GateSegmentSelects(working, gate, rows ?? RowShape.Unknown);

        // The strict tier checks the bill once every field has passed its gate. It drops nothing, so a
        // request still here names no field the caller may not use, and a weighted field the caller may
        // not use has already been refused as a denied one, before its weight could set it apart from a
        // name that matches nothing.
        if (gate.HidesExistence)
        {
            gate.CheckCost(cost);
        }

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
    private static void GateSegmentSelects(Segment segment, Gate gate, RowShape rows)
    {
        if (segment.Selects is null || segment.Selects.Count == 0)
        {
            if (SynthesizedProjection(gate, rows) is List<string> synthesized)
            {
                segment.Selects = synthesized;
            }

            return;
        }

        List<string> kept = GateProjection(segment.Selects, gate, rows);

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
    /// <para>
    /// The sets are counted first and on their own. A set with no conditions spends nothing from
    /// the condition budget and still adds to the statement the segment becomes, so the number of
    /// sets is the only thing that bounds that statement.
    /// </para>
    /// </remarks>
    private static void EnforceCaps(Segment segment, Gate gate)
    {
        gate.CheckConditionSetCount(segment.ConditionSets?.Count ?? 0);

        int conditions = 0;

        if (segment.ConditionSets is not null)
        {
            foreach (ConditionSet set in segment.ConditionSets)
            {
                conditions += CountConditions(set.ConditionGroup);
            }
        }

        gate.CheckConditionCount(conditions);

        if (segment.ConditionSets is not null)
        {
            foreach (ConditionSet set in segment.ConditionSets)
            {
                gate.CheckConditionDepth(Depth(set.ConditionGroup));
            }
        }

        int values = 0;

        if (segment.ConditionSets is not null)
        {
            foreach (ConditionSet set in segment.ConditionSets)
            {
                values = Math.Max(values, MostValues(set.ConditionGroup));
            }
        }

        gate.CheckConditionValues(values);
        gate.CheckOrderCount(segment.Orders?.Count ?? 0);
        gate.CheckPage(segment.Page);
    }

    /// <summary>
    /// Refuses a segment naming a path that crosses more navigations than the cap allows.
    /// </summary>
    /// <remarks>
    /// After canonicalization rather than with the counts, because only a canonical path says how many
    /// navigations it really crosses.
    /// </remarks>
    private static void EnforceNavigationDepth(Segment segment, Gate gate)
    {
        foreach (string field in SegmentFields(segment))
        {
            gate.CheckDepth(field);
        }
    }

    /// <summary>
    /// Supplies the page a guarded query was not given, where the deployment configured one.
    /// </summary>
    /// <remarks>
    /// <c>MaxPageSize</c> reads a page the caller sent, so a request with none was the one request
    /// no cap applied to: it returned the whole table while the same request naming that page size
    /// was refused. <see cref="DwCaps.DefaultPageSize"/> is off unless a deployment sets it, because
    /// filling one in changes what an existing caller receives.
    /// <para>
    /// Bounded by <c>MaxPageSize</c>, so the two cannot be configured into contradicting each other:
    /// a default above the maximum would hand out a page the same query could not have asked for.
    /// </para>
    /// </remarks>
    private static PageBy? DefaultPage(PageBy? page, DwPolicyOptions options)
    {
        if (page is not null || options.Caps.DefaultPageSize == 0)
        {
            return page;
        }

        return new PageBy
        {
            PageNumber = 1,
            PageSize = Math.Min(options.Caps.DefaultPageSize, options.Caps.MaxPageSize)
        };
    }

    /// <summary>
    /// Refuses a filter that exceeds a configured limit.
    /// </summary>
    /// <remarks>
    /// Run before any name is resolved, and so before any policy is. Caps exist to stop a request
    /// generating unbounded work, and resolving every name and policy of an unbounded filter is exactly
    /// the work being avoided, so the cheap structural check has to come first. Navigation depth is the
    /// one cap that needs a canonical path, and is checked once names are resolved.
    /// <para>
    /// Every cap refuses in both tiers, and none of them clamps. Truncating a filter would widen
    /// its result the way dropping a condition does, and silently shrinking a page would make a
    /// caller stepping through results skip rows while every call reported success.
    /// </para>
    /// </remarks>
    private static void EnforceCaps(Filter filter, Gate gate)
    {
        gate.CheckConditionCount(CountConditions(filter.ConditionGroup));
        gate.CheckConditionDepth(Depth(filter.ConditionGroup));
        gate.CheckConditionValues(MostValues(filter.ConditionGroup));
        gate.CheckOrderCount(filter.Orders?.Count ?? 0);
        gate.CheckPage(filter.Page);
    }

    /// <summary>
    /// Refuses a filter naming a path that crosses more navigations than the cap allows.
    /// </summary>
    /// <remarks>
    /// After canonicalization rather than with the counts, because only a canonical path says how many
    /// navigations it really crosses.
    /// </remarks>
    private static void EnforceNavigationDepth(Filter filter, Gate gate)
    {
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
        gate.CheckConditionDepth(Math.Max(Depth(summary.ConditionGroup), Depth(summary.Having)));
        gate.CheckConditionValues(Math.Max(MostValues(summary.ConditionGroup), MostValues(summary.Having)));
        gate.CheckAggregateCount(summary.GroupBy?.AggregateBy?.Count ?? 0);
        gate.CheckOrderCount(summary.Orders?.Count ?? 0);
        gate.CheckPage(summary.Page);
    }

    /// <summary>
    /// Refuses a summary naming a path that crosses more navigations than the cap allows.
    /// </summary>
    /// <remarks>
    /// After canonicalization rather than with the counts, because only a canonical path says how many
    /// navigations it really crosses.
    /// </remarks>
    private static void EnforceNavigationDepth(Summary summary, Gate gate)
    {
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
    /// What a filter spends from the budget.
    /// </summary>
    /// <remarks>
    /// After <see cref="EnforceCaps(Filter, Gate)"/> rather than beside it, and for the opposite
    /// reason to the one that puts the structural caps first: this one has to resolve a policy for
    /// every field the caller named, which is exactly the unbounded work the structural caps exist
    /// to stop. They run first and bound how many fields this pass will ever look at.
    /// <para>
    /// Before gating, so a field is charged for being named rather than for surviving. Charging
    /// only what survives would let a caller measure the policy by watching the budget: a name that
    /// costs nothing is a name that was dropped. The convenience tier refuses the bill there too; the
    /// strict tier, which drops nothing, refuses it after the gates, so a weighted field the caller may
    /// not use is refused as denied before its weight can tell it from a name that matches nothing.
    /// </para>
    /// <para>
    /// Selects and orders the caller wrote are charged; a projection this library synthesizes for a
    /// caller who named none is not, because charging for every field of a type would refuse a
    /// query nobody made expensive.
    /// </para>
    /// </remarks>
    private static long Cost(Filter filter, Gate gate)
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

        return budget.Total;
    }

    /// <summary>
    /// What a summary spends from the budget.
    /// </summary>
    /// <remarks>
    /// <c>Having</c> and <c>Orders</c> name aggregate aliases and dot-stripped group-by keys rather
    /// than property paths, so they are not charged here — the fields they stand for are charged
    /// through <c>GroupBy</c>, and charging both would bill the caller twice for one column.
    /// <para>
    /// An aggregate with no field, a <c>Count</c>, is charged the standing default. It names nothing a
    /// weight could be set on, and free, any number of them was a column of every group for nothing.
    /// </para>
    /// </remarks>
    private static long Cost(Summary summary, Gate gate)
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
                if (string.IsNullOrWhiteSpace(aggregate.Field))
                {
                    budget.ChargeDefault(gate);
                }
                else
                {
                    budget.Charge(aggregate.Field, gate);
                }
            }
        }

        return budget.Total;
    }

    /// <summary>
    /// What a set operation spends from the budget.
    /// </summary>
    /// <remarks>
    /// One budget across every set rather than one per set. A per-set budget would be spent as many
    /// times as the caller cares to add sets, which is the same unbounded work with more typing.
    /// </remarks>
    private static long Cost(Segment segment, Gate gate)
    {
        Budget budget = new();

        foreach (string field in SegmentFields(segment))
        {
            budget.Charge(field, gate);
        }

        return budget.Total;
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

        /// <summary>Charges one reference to nothing a weight could be set on, at the standing default.</summary>
        internal void ChargeDefault(Gate budget) => Total += budget.Options.Caps.DefaultFieldCost;
    }

    /// <summary>
    /// Counts every condition in a group and everything nested beneath it.
    /// </summary>
    /// <remarks>
    /// Counting the root group alone would let any budget be evaded by nesting, which is the
    /// cheapest way to rebuild the unbounded join the cap exists to prevent.
    /// </remarks>
    /// <summary>
    /// How deep a group tree nests, counting the root as one.
    /// </summary>
    /// <remarks>
    /// Measured rather than counted while walking, because the walk that gates conditions runs after
    /// the caps and the whole point of a cap is to refuse before that work starts.
    /// </remarks>
    private static int Depth(ConditionGroup? group)
    {
        if (group is null)
        {
            return 0;
        }

        int deepest = 0;

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in group.SubConditionGroups)
            {
                int depth = Depth(sub);

                if (depth > deepest)
                {
                    deepest = depth;
                }
            }
        }

        return deepest + 1;
    }

    /// <summary>The most values any one condition in a group tree carries.</summary>
    private static int MostValues(ConditionGroup? group)
    {
        if (group is null)
        {
            return 0;
        }

        int most = 0;

        if (group.Conditions is not null)
        {
            foreach (Condition condition in group.Conditions)
            {
                most = Math.Max(most, condition.Values?.Count ?? 0);
            }
        }

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup sub in group.SubConditionGroups)
            {
                most = Math.Max(most, MostValues(sub));
            }
        }

        return most;
    }

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
    /// The type's declared default order, less every field this caller may not order by.
    /// </summary>
    /// <remarks>
    /// A field left out is recorded, never refused. The caller did not send it, and a query refused
    /// over an order it never asked for is not a refusal anyone can act on. Leaving it in would rank
    /// the rows by a value the caller may not see. A dry run keeps it, as it keeps everything else,
    /// and still records what enforcement would have left out.
    /// <para>
    /// A field the default keeps is a use of that field, and an audited one is recorded as a caller's
    /// own order is. A field left out is not recorded: the query does not order by it and the caller
    /// never named it, so an event for it would say this caller tried to.
    /// </para>
    /// </remarks>
    /// <param name="gate">The per-query state.</param>
    /// <param name="segment">True for a segment, where a field denied for segments is left out too.</param>
    private static List<OrderBy> DefaultOrders<T>(Gate gate, bool segment = false) where T : class
    {
        List<DefaultOrder.Entry> kept = new();

        foreach (DefaultOrder.Entry entry in DefaultOrder.For(typeof(T)))
        {
            // Resolved without recording a use, which is recorded below only for a field the query keeps.
            FieldPolicy policy = gate.PolicyFor(entry.Field, PolicyFeature.None);

            // Inside a segment a field is refused anywhere it is denied for segments, an order the
            // caller sends included, so the default leaves it out there as well.
            if (!policy.Allows(PolicyFeature.Order) || (segment && !policy.Allows(PolicyFeature.Segment)))
            {
                gate.SkipDefaultOrder(entry.Field, policy);

                if (!gate.IsDryRun)
                {
                    continue;
                }
            }

            gate.PolicyFor(entry.Field, PolicyFeature.Order);

            kept.Add(entry);
        }

        return DefaultOrder.ToOrders(kept);
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
    private static void GateSelects(Filter filter, Gate gate, bool synthesize, RowShape rows)
    {
        if (filter.Selects is null || filter.Selects.Count == 0)
        {
            // Skipped when the caller is composing one clause at a time rather than sending a whole
            // filter: a lone Where or Order carries no projection, and synthesizing one for it would
            // refuse the call outright on a type whose every field is denied for select.
            if (synthesize)
            {
                SynthesizeSelects(filter, gate, rows);
            }

            return;
        }

        List<string> kept = GateProjection(filter.Selects, gate, rows);

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
    /// <para>
    /// A narrowing the core cannot build as written is refused rather than handed over. The builder
    /// adds the key of every node it narrows, so dropping a denied key from the list put it straight
    /// back; and a path through a collection the core does not unwrap, such as an
    /// <c>IReadOnlyList&lt;T&gt;</c>, failed validation with an error about the caller's field names.
    /// </para>
    /// </remarks>
    private static List<string> GateProjection(IEnumerable<string> selects, Gate gate, RowShape rows)
    {
        List<string> kept = new();

        void Keep(string path)
        {
            if (!kept.Contains(path, StringComparer.Ordinal))
            {
                kept.Add(path);
            }
        }

        foreach (string field in selects)
        {
            // The field's own decision first. A navigation that is itself denied is refused as it
            // stands; what it carries is only asked about once it has survived on its own account.
            FieldPolicy own = gate.PolicyFor(field, PolicyFeature.Select);

            if (!own.Allows(PolicyFeature.Select)
                && gate.Refuse(field, PolicyFeature.Select, PolicyErrorCode.FieldDeniedForSelect, own))
            {
                continue;
            }

            if (!gate.IsNavigation(field))
            {
                Keep(field);
                KeepCarriedKeys(field, gate, Keep);

                continue;
            }

            (List<string> survivors, List<(string Path, FieldPolicy Policy)> denied) =
                gate.Beneath(field, PolicyFeature.Select);

            // Each denied path, as a denial beneath a named navigation always was: refused under the
            // strict tier, dropped from the narrowing otherwise, left in place in a dry run.
            (string Path, FieldPolicy Policy)? cause = null;

            foreach ((string path, FieldPolicy policy) in denied)
            {
                if (gate.Refuse(path, PolicyFeature.Select, PolicyErrorCode.FieldDeniedForSelect, policy))
                {
                    cause ??= (path, policy);
                }
            }

            // A member can carry a denied field no path names: beyond the walker's depth, inside a
            // framework collection, declared by a subtype, or, under a policy that denies whatever it
            // does not name, anywhere the walk does not reach. On an entity, only what loads counts.
            bool hidden = cause is null
                          && gate.Unnamed(field, rows).DeniesSelect
                          && gate.RefuseHidden(field);

            if (cause is null && !hidden && !gate.ReadOnlyTransformBeneath(field))
            {
                Keep(field);

                // Kept whole, a navigation reached through others still passes through their nodes, and
                // the builder adds each one's key.
                KeepCarriedKeys(field, gate, Keep);

                continue;
            }

            (string Path, FieldPolicy? Policy) blame = cause is { } found ? (found.Path, found.Policy) : (field, null);

            if (!rows.Narrows(field.Split(SegmentSeparator)[0]))
            {
                // Narrowed only to keep a transform off a property it could not write: nothing is withheld
                // by keeping it whole, as it always was kept.
                if (cause is null && !hidden)
                {
                    Keep(field);
                    KeepCarriedKeys(field, gate, Keep);

                    continue;
                }

                gate.RefuseNarrowing(blame.Path, blame.Policy, $"'{field}' cannot be narrowed: {rows.WhyNotNarrowed(field)}");

                continue;
            }

            List<string> narrowed = new(survivors.Count);

            foreach (string path in survivors)
            {
                // Projected whole by the core, a field holding a framework collection of a policed type
                // carries the denied fields the policy cannot name inside it.
                if (gate.DeclaredType(path) is { } leaf && !Gate.HoldsValue(leaf) && gate.Unnamed(path, rows).DeniesSelect
                    && gate.RefuseHidden(path))
                {
                    continue;
                }

                narrowed.Add(path);
            }

            if (gate.DeniedKey(narrowed) is { } key)
            {
                gate.RefuseNarrowing(
                    key.Path, key.Policy,
                    $"'{field}' was narrowed, and the projection builder adds this key to every node it narrows");

                continue;
            }

            if (gate.Unprojectable(narrowed) is { } unbuilt)
            {
                gate.RefuseNarrowing(
                    blame.Path, blame.Policy,
                    $"'{field}' cannot be narrowed around it: the core cannot project '{unbuilt}'");

                continue;
            }

            foreach (string path in narrowed)
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
    /// A caller who sends no projection gets the whole row, denied columns included, because
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
    /// This never throws in the strict tier for a denied field. Strict refuses what a caller asks
    /// for, and here the caller named nothing; throwing would fail every strict query against a
    /// type carrying any denied field at all.
    /// </para>
    /// </remarks>
    private static void SynthesizeSelects(Filter filter, Gate gate, RowShape rows)
    {
        if (SynthesizedProjection(gate, rows) is List<string> allowed)
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
    /// <para>
    /// What an unguarded call would return, less what the policy withholds. A member that holds a
    /// value is kept when it is allowed. A member that holds an object, or a list of them, is kept
    /// whole when nothing beneath it is denied, and narrowed the way a caller naming it would have it
    /// narrowed when something is — but only where the source carries it: every member of a
    /// projected row or a row in memory, and the owned and complex members of an entity. An entity's
    /// other navigations are left out, since an unguarded call loads them only when something includes
    /// them, and projecting one would load it.
    /// </para>
    /// <para>
    /// Everything beneath every object member is asked about, whether or not the source carries it.
    /// A denial only beneath a member used to synthesize nothing, so the row came back whole, and an
    /// included navigation, an owned member, a projected list or an object in memory carried the
    /// denied value out with it. An entity query does not say what it loads: an include, an automatic
    /// include and a lazy loader all fill a navigation the query text never names.
    /// </para>
    /// <para>
    /// A member that cannot be narrowed as the projection would build it is left out whole: in
    /// memory, where the core's narrowing of a reference reads it through EF Core; an EF Core complex
    /// property, which the narrowing compares to null; where the builder would add a denied key back;
    /// and where the core cannot project a path beneath it.
    /// </para>
    /// </remarks>
    private static List<string>? SynthesizedProjection(Gate gate, RowShape rows)
    {
        List<string> allowed = new();
        List<(string Member, string Reason)> uncarried = new();
        bool anyDenied = false;
        int recorded = gate.TraceCount;

        foreach (PropertyInfo member in gate.Members())
        {
            string name = member.Name;
            FieldPolicy policy = gate.PolicyFor(name, PolicyFeature.None);

            if (!policy.Allows(PolicyFeature.Select))
            {
                anyDenied = true;

                gate.Record(name, PolicyFeature.Select, PolicyAction.Dropped, policy);

                continue;
            }

            // Two members share the name, one hidden with new under another type or spelled in another case:
            // the core reads one of them, and a row carries both. What either can hold is asked about, and a
            // projection, which cannot tell them apart, leaves the name out.
            if (gate.SharedName(name, rows) is { Shared: true } shared
                && (shared.Denies || gate.Beneath(name, PolicyFeature.None).Denied.Any(d => rows.Materializes(d.Path))))
            {
                anyDenied = true;

                gate.LeaveOut(name, "left out: the type declares more than one member of this name, which a projection cannot tell apart");

                continue;
            }

            // A projection assigns only a member with a setter, and no path can name a member the
            // expression parser keeps a word for; such a member is still asked about below.
            bool assignable = member.CanWrite && !ReservedNames.Starts(name);

            if (Gate.HoldsValue(member.PropertyType))
            {
                if (assignable && rows.CarriesValue(name))
                {
                    allowed.Add(name);
                }

                continue;
            }

            (List<string> survivors, List<(string Path, FieldPolicy Policy)> denied) =
                gate.Beneath(name, PolicyFeature.None);

            // Only a denial whose value can reach the result asks for a projection: one beneath a
            // navigation nothing loads never leaves the database. What no path names is asked about
            // only where the source carries the member at all, and on an entity only where it loads.
            List<(string Path, FieldPolicy Policy)> reached = denied.Where(d => rows.Materializes(d.Path)).ToList();
            Gate.TypeFacts unnamed = rows.Materializes(name + SegmentSeparator + name)
                ? gate.Unnamed(name, rows)
                : default;
            bool forced = gate.ForcedBeneath(name);

            foreach ((string path, FieldPolicy beneath) in reached)
            {
                gate.Record(path, PolicyFeature.Select, PolicyAction.Dropped, beneath);
            }

            // A forced scope beneath a member does not ask for a projection on its own. It filters the
            // rows that hold the member, which is what it has always done for a list returned whole,
            // and asking would leave out every included list of a scoped child type. Nor does a member
            // that can hold an object of any type: the policy cannot see into it whether or not a
            // projection is built, and asking would drop every such column of every query.
            if (reached.Count > 0 || unnamed.DeniesSelect)
            {
                anyDenied = true;
            }

            if (!assignable || !rows.Carries(name))
            {
                // Left out because a projection cannot keep it. Worth a line in the trace only where the
                // unguarded call would have returned it: an included navigation, an object in memory.
                if (rows.Materializes(name + SegmentSeparator + name))
                {
                    uncarried.Add((name, !assignable
                        ? "left out: a projection cannot assign it"
                        : rows.Kind == RowKind.InMemory
                            ? "left out: a projection cannot keep an object a row in memory holds"
                            : "left out: it is a navigation, which projecting would load"));
                }

                continue;
            }

            if (reached.Count == 0 && !unnamed.Opaque && !forced && !gate.ReadOnlyTransformBeneath(name))
            {
                allowed.Add(name);

                continue;
            }

            string? reason = forced
                ? "left out whole: a scope forced beneath it cannot be applied to what it holds"
                : rows.Narrows(name)
                    ? Narrowed(name, survivors, gate, rows, allowed)
                    : reached.Count == 0 && !unnamed.DeniesSelect && unnamed.HoldsObject
                        ? "left out whole: it can hold what the policy cannot name"
                        : "left out whole: " + rows.WhyNotNarrowed(name);

            if (reason is not null)
            {
                gate.LeaveOut(name, reason);
            }
        }

        // A row can be a subtype of T, whose own members T's never name. The projection builds T, so it
        // leaves them out; one this caller may not have is what asks for it.
        foreach (string path in gate.DerivedDenials(rows))
        {
            anyDenied = true;

            gate.LeaveOut(path, "left out: a type derived from the row's type declares it, and the projection builds the row's type");
        }

        // A member a base type declares and T hides with new is still held by a row in memory, and read by
        // anyone reading the row as the base type. A projection builds T and leaves it out.
        if (rows.Kind == RowKind.InMemory)
        {
            foreach (string name in gate.HiddenDenials())
            {
                anyDenied = true;

                gate.LeaveOut(name, "left out: a base type's member of this name, which the row's own member hides");
            }
        }

        // A member that asks for nothing itself is still left out, or narrowed, as the projection would build
        // it. With no projection built nothing was, and the trace says what the query did.
        if (!anyDenied)
        {
            gate.WithdrawTrace(recorded);

            return null;
        }

        if (gate.IsDryRun)
        {
            return null;
        }

        foreach ((string name, string reason) in uncarried)
        {
            gate.LeaveOut(name, reason);
        }

        if (allowed.Count == 0)
        {
            throw gate.Exception(WholeClause, PolicyFeature.Select, PolicyErrorCode.AllSelectsDenied, null);
        }

        return allowed;
    }

    /// <summary>
    /// Narrows a member for a synthesized projection, adding what survives to <paramref name="allowed"/>,
    /// or returns why the member is left out whole instead.
    /// </summary>
    /// <remarks>
    /// A field whose type can hold what the policy cannot name, a framework collection of a policed
    /// type or a member typed <see cref="object"/>, is left out of the narrowing: the core projects it
    /// whole. The builder adds the key of every node it narrows, so a denied key leaves the member out,
    /// and so does a path the core cannot project.
    /// </remarks>
    private static string? Narrowed(string member, List<string> survivors, Gate gate, RowShape rows, List<string> allowed)
    {
        List<string> kept = new(survivors.Count);

        foreach (string path in survivors)
        {
            if (gate.DeclaredType(path) is { } type && !Gate.HoldsValue(type) && gate.Unnamed(path, rows).Opaque)
            {
                gate.LeaveOut(path, "left out whole: it can hold what the policy cannot name");

                continue;
            }

            kept.Add(path);
        }

        if (gate.DeniedKey(kept) is { } key)
        {
            return $"left out whole: the projection would add its key '{key.Path}', which is denied";
        }

        if (gate.Unprojectable(kept) is { } unbuilt)
        {
            return $"left out whole: the core cannot project '{unbuilt}'";
        }

        if (gate.Unbuildable(member, kept) is { } node)
        {
            return $"left out whole: the core cannot build '{node.Name}', which it narrows into";
        }

        if (kept.Count == 0)
        {
            return "left out whole: nothing beneath it may be selected";
        }

        allowed.AddRange(kept);

        return null;
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

        // A predicate that lets null through is a disjunction, so it cannot sit among the root's own
        // conditions: it goes in a group of its own, still joined to everything else by And.
        List<ConditionGroup> widened = new();

        foreach (ForcedPredicate predicate in gate.TypePolicy.Forced)
        {
            Condition? condition = gate.Materialize<T>(predicate, injected.Count);

            if (condition is null)
            {
                continue;
            }

            if (Widens<T>(predicate, condition.Field!))
            {
                condition.Sort = 0;

                widened.Add(new ConditionGroup
                {
                    // Zero is the caller's group.
                    Sort = widened.Count + 1,
                    Connector = Connector.Or,
                    Conditions = new List<Condition>
                    {
                        condition,
                        new()
                        {
                            Sort = 1,
                            Field = condition.Field,
                            DataType = condition.DataType,
                            Operator = Operator.IsNull
                        }
                    },
                    SubConditionGroups = new List<ConditionGroup>()
                });

                continue;
            }

            injected.Add(condition);
        }

        if (injected.Count == 0 && widened.Count == 0)
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

        root.SubConditionGroups.AddRange(widened);

        return root;
    }

    /// <summary>
    /// True when a forced predicate is injected as <c>(field op value OR field IS NULL)</c>.
    /// </summary>
    /// <remarks>
    /// Only where the field can actually be null. The attribute refuses <c>AllowNull</c> on a member
    /// that never can be, but a runtime rule is written without the type to hand, and on such a member
    /// the widening could never match a row: the comparison alone is the same predicate, and the one
    /// that keeps the query plan simple. A path through a navigation can always be null, because the
    /// navigation can be absent.
    /// </remarks>
    private static bool Widens<T>(ForcedPredicate predicate, string path) where T : class
    {
        // A null check compares against nothing, and widening one would test the field for null and for
        // not null at once: no filter at all. Refused where the predicate is built; never widened here.
        if (!predicate.AllowNull || predicate.IsNullCheck)
        {
            return false;
        }

        if (path.IndexOf(SegmentSeparator) >= 0)
        {
            return true;
        }

        Type? type = CacheReflection.FindProperty(typeof(T), path)?.PropertyType;

        return type is null || !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
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
    /// A name matching nothing is handed to <c>Validate&lt;T&gt;()</c> unchanged under the convenience
    /// tier and in a dry run, so it fails with the error an unguarded query would give. Under the
    /// strict tier it is marked unknown instead, and refused at the same step and with the same code
    /// as a field the caller may not use. A caller therefore cannot probe for which fields exist by
    /// watching how the policy layer refuses them.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="name">The name as the caller wrote it.</param>
    /// <param name="gate">The per-query state, carrying the type's alias map.</param>
    /// <param name="computed">
    /// True when the clause needs the provider to compute the path — a filter, an order, a grouping
    /// key, an aggregated field. False for a projection, which the provider evaluates on the client
    /// when it cannot translate it, so a member no database can compute is still returned there.
    /// </param>
    /// <returns>The canonical field path.</returns>
    /// <exception cref="PolicyException">Thrown when the name could mean more than one field.</exception>
    /// <exception cref="LogicException">Thrown when the name names nothing, outside the strict tier.</exception>
    private static string ResolveName<T>(string name, Gate gate, bool computed = true) where T : class
    {
        // A type nobody has aliased takes the path it always did, with no extra reflection and no
        // behavioural difference from before this existed.
        if (gate.TypePolicy.Aliases.Count == 0)
        {
            return gate.HidesExistence
                ? Expressible(TryValidate<T>(name), name, gate, computed) ?? gate.Unknown(name)
                : name.Validate<T>();
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
            // Nothing matched. Hand it back to the validator so the failure is the ordinary one --
            // except under the strict tier, where it is gated like a field denied for everything.
            return gate.HidesExistence ? gate.Unknown(name) : name.Validate<T>();
        }

        string canonical = candidates[0];

        if (gate.HidesExistence && Expressible(canonical, spoken, gate, computed) is null)
        {
            return gate.Unknown(spoken);
        }

        gate.RecordSpelling(canonical, spoken);

        return canonical;
    }

    /// <summary>
    /// The canonical path when the source can express it, and null when the source provably cannot.
    /// </summary>
    /// <remarks>
    /// A member exists on the row's type and still has no value the provider can compute:
    /// <c>Name.IsEmpty</c> over a localized text, a getter over two columns. Every check the policy
    /// makes passes, and the query then fails inside the provider — a five-hundred where the strict
    /// tier promises a refusal, and the one place the tier answers a caller with something other
    /// than an answer or a refusal.
    /// <para>
    /// Only the strict tier reaches this, and only where <see cref="RowShape.Expresses"/> can prove
    /// the path wrong. Elsewhere the name is returned unchanged and fails exactly as it does today,
    /// which is what an unguarded query does with it.
    /// </para>
    /// <para>
    /// The refusal is the unknown-name one, so a caller cannot tell a member that does not exist
    /// from one that exists and cannot be computed, any more than they can tell either from a field
    /// they may not use.
    /// </para>
    /// </remarks>
    private static string? Expressible(string? canonical, string spoken, Gate gate, bool computed)
    {
        if (canonical is null)
        {
            return null;
        }

        if (!computed)
        {
            // A projection is the last thing the provider builds, and EF Core evaluates that one on
            // the client when it cannot translate it. So a member no database can compute is still
            // a member a caller can select, and refusing it here would take back a projection that
            // has always worked.
            return canonical;
        }

        if (gate.Rows.Expresses(canonical) == false)
        {
            gate.RecordUnexpressible(canonical, spoken);

            return null;
        }

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
                filter.Selects[i] = ResolveName<T>(filter.Selects[i], gate, computed: false);
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

        // Names that match nothing on the type, kept under the strict tier so they can be refused
        // where a denied field is, as a denied field is.
        private readonly HashSet<string> _unknown = new(StringComparer.Ordinal);

        // What a projection of each navigation carries, per root type and path. Reflection only, so it
        // is the same for every caller and every query.
        private static readonly ConcurrentDictionary<(Type Root, string Path), IReadOnlyList<string>> CarriedPaths = new();

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

        /// <summary>
        /// True while a segment is sanitized, where every field refusal of the strict tier carries the
        /// segment's code.
        /// </summary>
        internal bool InSegment { get; init; }

        /// <summary>
        /// What the source produces, which decides whether a path the caller named is something the
        /// query can express at all.
        /// </summary>
        internal RowShape Rows { get; init; } = RowShape.Unknown;

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
                _trace.RecordSpelling(canonicalPath, spoken);
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
        /// True when a name that matches nothing, and a field this caller may not use, must be told
        /// apart by nothing in the answer.
        /// </summary>
        /// <remarks>
        /// The strict tier serves a caller who may be probing. Refusing an unknown name as a validation
        /// error and a denied field as a policy refusal hands that caller the list of columns they
        /// cannot see, one guess at a time, and a refusal carrying the field's path or the attribute
        /// that sealed it confirms each guess. So an unknown name is gated as a field denied for every
        /// feature, at the step a denial is raised, and every field refusal names no field and no
        /// source. The trace and the audit keep both. A dry run refuses nothing, so an unknown name
        /// still fails there as it would unguarded.
        /// </remarks>
        internal bool HidesExistence => IsStrict && !IsDryRun;

        /// <summary>
        /// Records that a path exists on the type and names no value the query can compute, before
        /// it is refused as an unknown name would be.
        /// </summary>
        /// <remarks>
        /// The refusal itself says nothing, on purpose, so the trace is where an operator reads what
        /// happened. Without it, a filter on <c>Name.IsEmpty</c> would be refused with the same
        /// wording as a misspelling and nothing anywhere would say which of the two it was.
        /// </remarks>
        internal void RecordUnexpressible(string canonicalPath, string spoken)
        {
            _trace.RecordSpelling(canonicalPath, spoken);

            _trace.Add(new PolicyDecision(
                canonicalPath,
                PolicyFeature.None,
                PolicyAction.Denied,
                "the member exists on the type and the query cannot compute it, so it is refused as an unknown name is"));
        }

        /// <summary>Remembers a name that matches nothing, and returns it for the gate to refuse.</summary>
        /// <remarks>
        /// Empty and blank segments are dropped, as they are from a real path before it is counted. A
        /// real field padded with dots canonicalizes to its own depth; an unknown name kept padded would
        /// fail the navigation cap instead, and so tell the two apart.
        /// </remarks>
        internal string Unknown(string name)
        {
            string collapsed = string.Join(
                SegmentSeparator,
                name.Split(SegmentSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            string spoken = collapsed.Length > 0 ? collapsed : name.Trim();

            _unknown.Add(spoken);

            return spoken;
        }

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
        /// The members of the entity: every readable property that is not an indexer.
        /// </summary>
        /// <remarks>
        /// Read through the same reflection cache the validator uses, so a synthesized projection
        /// cannot name a field the pipeline would then reject. A projection assigns only the writable
        /// ones, but a read-only one is still asked about: a denial on it, or beneath it, still needs a
        /// projection, or the row comes back holding it.
        /// </remarks>
        internal IEnumerable<PropertyInfo> Members()
        {
            foreach (KeyValuePair<string, PropertyInfo> entry in CacheReflection.GetTypeProperties(_entityType))
            {
                PropertyInfo property = entry.Value;

                if (property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    yield return property;
                }
            }
        }

        /// <summary>
        /// True when a member of this type holds a value rather than an object: a scalar, or a
        /// collection of scalars such as <c>List&lt;string&gt;</c> or <c>byte[]</c>.
        /// </summary>
        /// <remarks>
        /// A collection of scalars is a value the projection assigns whole. Nothing beneath it can
        /// carry a policy, and on an entity it is a column the unguarded call loads.
        /// </remarks>
        internal static bool HoldsValue(Type type) =>
            CacheReflection.IsSimpleType(AttributePolicyProvider.Peeled(type))
            && !AttributePolicyProvider.Layers(type).Any(layer => OwnsMembers(layer) && Facts(layer).DeniesSelect);

        /// <summary>
        /// True for an application's own collection class, one deriving from <c>List&lt;string&gt;</c> or a
        /// <c>Dictionary</c> say, that declares members beside the elements it holds. A collection of values
        /// stays a value unless a member of its own is denied.
        /// </summary>
        private static bool OwnsMembers(Type type) =>
            type.IsClass
            && !type.IsArray
            && !AttributePolicyProvider.IsFramework(type)
            && AttributePolicyProvider.Peeled(type) != type
            && type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(property => property.GetIndexParameters().Length == 0
                                 && property.DeclaringType is { } declaring
                                 && !AttributePolicyProvider.IsFramework(declaring));

        /// <summary>
        /// The names of members a base type of T declares, T hides with <c>new</c>, and this caller may not have,
        /// or that hold a denied field no path names.
        /// </summary>
        internal List<string> HiddenDenials()
        {
            List<string> found = new();
            PropertyInfo[] shown = _entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            for (Type? declaring = _entityType.BaseType; declaring is not null && declaring != typeof(object); declaring = declaring.BaseType)
            {
                foreach (PropertyInfo hidden in declaring.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!hidden.CanRead
                        || hidden.GetIndexParameters().Length != 0
                        || found.Contains(hidden.Name, StringComparer.Ordinal)
                        || shown.Any(member => member.Name == hidden.Name && SameSlot(member, hidden)))
                    {
                        continue;
                    }

                    if (DeclaresDenial(hidden)
                        || (!HoldsValue(hidden.PropertyType) && Carried(hidden.PropertyType, hidden.Name, exact: false).DeniesSelect))
                    {
                        found.Add(hidden.Name);
                    }
                }
            }

            return found;
        }

        /// <summary>True when two properties' getters are one method, or one overrides the other.</summary>
        private static bool SameSlot(PropertyInfo left, PropertyInfo right) =>
            left.GetGetMethod() is { } first
            && right.GetGetMethod() is { } second
            && first.GetBaseDefinition() is { } a
            && second.GetBaseDefinition() is { } b
            && a.MetadataToken == b.MetadataToken
            && a.Module == b.Module;

        /// <summary>The names more than one readable member of a type shares, compared as the core compares names.</summary>
        private static readonly ConcurrentDictionary<Type, HashSet<string>> SharedNames = new();

        private static HashSet<string> ReadSharedNames(Type type) =>
            new(type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                    .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether T declares more than one member of a name, as the core compares names, one of them holding
        /// more than a value; and if so, whether what one of them holds is denied.
        /// </summary>
        /// <remarks>
        /// A member hidden with <c>new</c> under another type, or two names differing only in case. The core
        /// reads one of them by name, and a row carries every one, which a serializer writes.
        /// </remarks>
        internal (bool Shared, bool Denies) SharedName(string name, RowShape rows)
        {
            if (!SharedNames.GetOrAdd(_entityType, ReadSharedNames).Contains(name))
            {
                return (false, false);
            }

            List<PropertyInfo> variants = _entityType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead
                                   && property.GetIndexParameters().Length == 0
                                   && string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (variants.TrueForAll(variant => HoldsValue(variant.PropertyType)))
            {
                return (false, false);
            }

            // An entity's model says which of them EF Core maps and what loads beneath it; any other row can
            // hold what either type can.
            bool beneath = rows.LoadedBeneath(name) is not null
                ? Unnamed(name, rows).DeniesSelect
                : variants.Exists(variant => !HoldsValue(variant.PropertyType)
                                             && Carried(variant.PropertyType, name, exact: false).DeniesSelect);

            return (true, beneath || variants.Exists(variant => Denies(name, variant)));
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
        /// A type is read the way <see cref="AttributePolicyProvider"/> reads it, through
        /// <see cref="AttributePolicyProvider.NavigationTypeOf"/>, and no path is longer than the
        /// longest the walker puts a fragment on. It once read collections through a narrower list of
        /// its own, so a member typed <c>IReadOnlyList&lt;T&gt;</c> hid every denial beneath it, and it
        /// went a level deeper than the walker, so a narrowing projected fields no policy could speak
        /// about. It does not follow a type back into itself along one path: a narrowing leaves such a
        /// region out.
        /// </para>
        /// </remarks>
        internal IReadOnlyList<string> ProjectionUnder(string path) =>
            CarriedPaths.GetOrAdd((_entityType, path), key => Enumerate(key.Root, key.Path));

        private static IReadOnlyList<string> Enumerate(Type root, string path)
        {
            List<string> paths = new();

            if (DeclaredType(root, path) is { } declared
                && AttributePolicyProvider.NavigationTypeOf(declared) is { } type)
            {
                Descend(path, type, path.Split(SegmentSeparator).Length, paths, new HashSet<Type>());
            }

            return paths;
        }

        // depth is how many segments prefix has; a child has one more.
        private static void Descend(
            string prefix, Type type, int depth, List<string> paths, HashSet<Type> seen)
        {
            // A child would be longer than any path the walker gives a fragment, or the type is one this
            // path already passed through and would otherwise enumerate until the stack ran out.
            if (depth >= AttributePolicyProvider.MaxDepth || !seen.Add(type))
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

                // A value, a collection of values, or a type the walker does not enter: projected
                // whole, and nothing beneath it has a path of its own.
                if (AttributePolicyProvider.NavigationTypeOf(property.PropertyType) is not { } nested)
                {
                    paths.Add(child);

                    continue;
                }

                Descend(child, nested, depth + 1, paths, seen);
            }

            seen.Remove(type);
        }

        /// <summary>
        /// What lies beneath a member, as the policy sees it: the fields a narrowing of it could keep,
        /// and every path beneath it this caller may not select.
        /// </summary>
        /// <param name="member">The member's path.</param>
        /// <param name="use">
        /// <see cref="PolicyFeature.Select"/> when the caller named the member, so an audited field a
        /// narrowing would carry is recorded as used; <see cref="PolicyFeature.None"/> otherwise.
        /// </param>
        /// <remarks>
        /// The denials come from two places. The walk above finds every field a projection of the
        /// member could hold. The fragments the providers hold find the rest: a fragment matches a path
        /// exactly or matches every path, so a denied path the walk never produced, beneath a property
        /// with no setter, around a cycle, or deeper than the walk goes, is still one of them.
        /// </remarks>
        internal (List<string> Survivors, List<(string Path, FieldPolicy Policy)> Denied) Beneath(
            string member, PolicyFeature use)
        {
            List<string> survivors = new();
            List<(string Path, FieldPolicy Policy)> denied = new();
            HashSet<string> asked = new(StringComparer.OrdinalIgnoreCase);

            foreach (string path in ProjectionUnder(member))
            {
                asked.Add(path);

                FieldPolicy policy = PolicyFor(path, use);

                if (policy.Allows(PolicyFeature.Select))
                {
                    survivors.Add(path);
                }
                else
                {
                    denied.Add((path, policy));
                }
            }

            string prefix = member + SegmentSeparator;

            foreach (PolicyFragment fragment in Fragments)
            {
                // A rule on a path the type does not have, one written for a member since removed, holds
                // nothing a projection could carry.
                if (fragment.IsWildcard
                    || !fragment.FieldPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !asked.Add(fragment.FieldPath)
                    || !Resolves(_entityType, fragment.FieldPath))
                {
                    continue;
                }

                FieldPolicy policy = PolicyFor(fragment.FieldPath, PolicyFeature.None);

                if (!policy.Allows(PolicyFeature.Select))
                {
                    denied.Add((fragment.FieldPath, policy));
                }
            }

            return (survivors, denied);
        }

        /// <summary>
        /// True when a path names a member, in any letter case, of the type or of any subtype a value along
        /// it can be: a rule on <c>Org.Swift</c> names a field of the bank a member declared as an
        /// organisation holds, and a type with both <c>Info</c> and <c>INFO</c> has two members the path
        /// could name.
        /// </summary>
        private static bool Resolves(Type root, string path)
        {
            List<Type> current = new() { root };
            string[] segments = path.Split(SegmentSeparator);

            for (int i = 0; i < segments.Length; i++)
            {
                List<Type> next = new();
                bool named = false;

                foreach (Type type in current)
                {
                    foreach (Type candidate in KnownSubtypes.Of(type).Prepend(type))
                    {
                        foreach (PropertyInfo property in candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (!string.Equals(property.Name, segments[i], StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            named = true;

                            if (AttributePolicyProvider.NavigationTypeOf(property.PropertyType) is { } nested
                                && !next.Contains(nested))
                            {
                                next.Add(nested);
                            }
                        }
                    }
                }

                if (!named || (i < segments.Length - 1 && next.Count == 0))
                {
                    return false;
                }

                current = next;
            }

            return true;
        }

        /// <summary>
        /// The first type a narrowing would build a node as that the core's typed projection cannot
        /// construct, an interface, an abstract class or one without a public parameterless constructor,
        /// which it skips; null when it can build them all.
        /// </summary>
        internal Type? Unbuildable(string member, IEnumerable<string> paths)
        {
            HashSet<string> nodes = new(StringComparer.OrdinalIgnoreCase) { member };

            foreach (string path in paths)
            {
                for (int cut = path.IndexOf(SegmentSeparator); cut >= 0; cut = path.IndexOf(SegmentSeparator, cut + 1))
                {
                    nodes.Add(path[..cut]);
                }
            }

            foreach (string node in nodes)
            {
                if (DeclaredType(node) is { } declared
                    && AttributePolicyProvider.NavigationTypeOf(declared) is { } type
                    && (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null))
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>Every fragment the providers hold for this type and caller, read once per query.</summary>
        private IReadOnlyList<PolicyFragment> Fragments => _fragments ??= _resolver.Fragments(_entityType, _context);

        private IReadOnlyList<PolicyFragment>? _fragments;

        /// <summary>
        /// True when a forced predicate reaches beneath the member. A projection cannot apply it to the
        /// elements the member holds: the scope filters the rows of the query, and a list kept whole or
        /// narrowed carries every element, those the scope excludes included.
        /// </summary>
        internal bool ForcedBeneath(string member)
        {
            string prefix = member + SegmentSeparator;

            return TypePolicy.Forced.Any(
                forced => forced.FieldPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// True when a transform beneath the member lands on a property with no setter, which the
        /// outbound walk could not write back: a narrowing leaves such a property out.
        /// </summary>
        internal bool ReadOnlyTransformBeneath(string member)
        {
            string prefix = member + SegmentSeparator;

            foreach (string path in TypePolicy.Transforms.Keys)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && Property(_entityType, path) is { CanWrite: false })
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// What the value of a member can carry that the paths the walk asks about do not name: a field
        /// denied for Select, and a member that can hold an object of any type.
        /// </summary>
        /// <remarks>
        /// On an entity query the model says what loads beneath the member
        /// (<see cref="RowShape.LoadedBeneath"/>). Each loaded member is asked about by its path, and by
        /// its own attribute where no fragment reaches it: deeper than the walker goes, or declared by a
        /// type the model derives. A value EF Core reads whole is read through its type. What EF Core
        /// materializes from the database holds no object of the application's; what a value converter
        /// hands back is the application's code, and holds what its type allows. A
        /// projection's member is read as the type the projection constructs it as, when it says. Any
        /// other value can carry whatever the member's type can hold, its subtypes included.
        /// Under a policy that denies every field it does not name, a path the walk never asks about is a
        /// denied one unless the policy names it.
        /// </remarks>
        internal TypeFacts Unnamed(string member, RowShape rows)
        {
            if (rows.LoadedBeneath(member) is { } loaded)
            {
                bool denies = false;
                bool holdsObject = false;

                foreach (Loaded entry in loaded)
                {
                    TypeFacts carried = entry.Whole && !HoldsValue(entry.Property.PropertyType)
                        ? Carried(entry.Property.PropertyType, entry.Path, exact: false)
                        : default;

                    denies |= Denies(entry.Path, entry.Property) || carried.DeniesSelect;
                    holdsObject |= entry.Converted && carried.HoldsObject;
                }

                return new TypeFacts(denies, holdsObject, loaded.Count > 0);
            }

            if (!member.Contains(SegmentSeparator) && rows.BuiltType(member) is { } built)
            {
                return Carried(built, member, exact: true);
            }

            // A path that names nothing is refused long before this; were it not, nothing is shown clean.
            return DeclaredType(member) is { } type
                ? Carried(type, member, exact: false)
                : new TypeFacts(true, true, true);
        }

        /// <summary>
        /// What a value of a type, held at a path, can carry that no path names. An exact type is the one
        /// the value was constructed as, so its own subtypes cannot be it.
        /// </summary>
        private TypeFacts Carried(Type type, string path, bool exact)
        {
            TypeFacts facts = Facts(type, exact);

            return DeniesByDefault && !Granted(type, path, exact)
                ? facts with { DeniesSelect = true }
                : facts;
        }

        /// <summary>
        /// True when this caller may not select a member reached along a path: its policy says so, or,
        /// where no fragment reaches the path, an attribute on the member does.
        /// </summary>
        /// <remarks>
        /// The walker puts a fragment on every path of the declared types up to its depth, a member
        /// with no setter, a path around a cycle, and a denial an override or an implementation declares
        /// included. Past its depth, or on a member only a derived type declares, the attributes are all
        /// there is, read from every declaration a row can run.
        /// </remarks>
        private bool Denies(string path, PropertyInfo property)
        {
            if (!PolicyFor(path, PolicyFeature.None).Allows(PolicyFeature.Select))
            {
                return true;
            }

            return (path.Split(SegmentSeparator).Length > AttributePolicyProvider.MaxDepth
                    || Property(_entityType, path) is null)
                   && DeclaresDenial(property);
        }

        /// <summary>
        /// True when a member's attributes deny Select: its own, inherited ones, and those of another
        /// declaration a row of the type it was read from can run, an interface member or a subtype's
        /// override among them. Read once for each member, and again once another assembly has loaded.
        /// </summary>
        private static bool DeclaresDenial(PropertyInfo property)
        {
            int epoch = KnownSubtypes.Epoch;

            if (DenialsByMember.TryGetValue(property, out (int Epoch, bool Denies) known) && known.Epoch == epoch)
            {
                return known.Denies;
            }

            bool denies = property.GetCustomAttributes<DwDenyAttribute>(inherit: true)
                .Concat(AttributePolicyProvider.DenialsElsewhere(property.ReflectedType ?? property.DeclaringType!, property))
                .Any(attribute => (attribute.Features & PolicyFeature.Select) != 0);

            DenialsByMember[property] = (epoch, denies);

            return denies;
        }

        private static readonly ConcurrentDictionary<PropertyInfo, (int Epoch, bool Denies)> DenialsByMember = new();

        /// <summary>
        /// Every member a type derived from T declares that a row can carry and this caller may not
        /// have, or that carries a denied field no path names. A projection builds T itself and leaves
        /// them all out, so each is a reason to project.
        /// </summary>
        /// <remarks>
        /// A projection builds the type its initializer constructs, which may be a subtype of T. An entity
        /// query materializes each row as the type the database says it is, which the model knows. Any
        /// other source can hold any subtype loaded.
        /// </remarks>
        internal List<string> DerivedDenials(RowShape rows)
        {
            List<string> found = new();

            if (rows.Kind == RowKind.Projected)
            {
                if (rows.Built is { } built && built != _entityType && _entityType.IsAssignableFrom(built))
                {
                    Declared(built, found);
                }

                return found;
            }

            if (rows.LoadedByDerived() is { } loaded)
            {
                foreach (Loaded entry in loaded)
                {
                    if (Denies(entry.Path, entry.Property)
                        || (entry.Whole && !HoldsValue(entry.Property.PropertyType)
                                        && Carried(entry.Property.PropertyType, entry.Path, exact: false).DeniesSelect))
                    {
                        found.Add(entry.Path);
                    }
                }

                return found;
            }

            foreach (Type subtype in KnownSubtypes.Of(_entityType))
            {
                Declared(subtype, found);
            }

            return found;
        }

        /// <summary>Adds each member a subtype declares below T that this caller may not have.</summary>
        private void Declared(Type subtype, List<string> found)
        {
            foreach (PropertyInfo property in subtype.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead
                    || property.GetIndexParameters().Length != 0
                    || property.DeclaringType!.IsAssignableFrom(_entityType)
                    || found.Contains(property.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                if (Denies(property.Name, property)
                    || (!HoldsValue(property.PropertyType)
                        && Carried(property.PropertyType, property.Name, exact: false).DeniesSelect))
                {
                    found.Add(property.Name);
                }
            }
        }

        /// <summary>
        /// True when a <c>"*"</c> rule denies Select on every field this caller is not granted by name,
        /// so that a path the walk never asks about is a denied one.
        /// </summary>
        internal bool DeniesByDefault =>
            _deniesByDefault ??= Fragments.Any(fragment => fragment.IsWildcard)
                                 && !PolicyFor(UnnamedPath, PolicyFeature.None).Allows(PolicyFeature.Select);

        private bool? _deniesByDefault;

        /// <summary>A path no member can have, so that only the <c>"*"</c> rules match it.</summary>
        private const string UnnamedPath = "<unnamed>";

        /// <summary>
        /// Under a policy denying every field it does not name, true when every path beneath a member of
        /// this type that the walk never asks about is one the policy grants by name.
        /// </summary>
        /// <remarks>
        /// The walk skips a property with no setter, stops four segments deep and at a type it already
        /// passed through, reads declared types only and takes a framework type whole. Each path it skips
        /// is asked about here, a subtype's members included; one the policy does not name is denied.
        /// Around a cycle the paths never end, and inside a framework type holding an application type's
        /// members no path can name them, so neither is ever granted.
        /// </remarks>
        private bool Granted(Type type, string path, bool exact)
        {
            if (!_granted.TryGetValue((type, path, exact), out bool granted))
            {
                granted = Grant(type, path, exact, new HashSet<Type>());
                _granted[(type, path, exact)] = granted;
            }

            return granted;
        }

        private readonly Dictionary<(Type, string, bool), bool> _granted = new();

        private bool Grant(Type type, string path, bool exact, HashSet<Type> onPath)
        {
            // A value, a collection of them among values, holds no path to name.
            if (HoldsValue(type))
            {
                return true;
            }

            if (AttributePolicyProvider.NavigationTypeOf(type) is not { } node)
            {
                TypeFacts facts = Facts(type, exact: false);

                return !facts.HoldsObject && !facts.HoldsMembers;
            }

            if (!onPath.Add(node))
            {
                return false;
            }

            int depth = path.Split(SegmentSeparator).Length;
            IEnumerable<(PropertyInfo Property, bool Declared)> members = node
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => (property, true));

            if (!exact)
            {
                members = members.Concat(KnownSubtypes.Of(node)
                    .SelectMany(subtype => subtype.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    .Where(property => !property.DeclaringType!.IsAssignableFrom(node))
                    .Select(property => (property, false)));
            }

            foreach ((PropertyInfo property, bool declared) in members)
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                string child = $"{path}{SegmentSeparator}{property.Name}";
                bool walked = declared && property.CanWrite && depth < AttributePolicyProvider.MaxDepth;

                if ((!walked && Denies(child, property))
                    || (!HoldsValue(property.PropertyType) && !Grant(property.PropertyType, child, false, onPath)))
                {
                    return false;
                }
            }

            onPath.Remove(node);

            return true;
        }

        /// <summary>
        /// What a type can hold that no path of the policy's reaches, read once per type and again once
        /// another assembly has loaded, since that can add subtypes. An exact type is the one a value was
        /// constructed as, so its own subtypes are not read.
        /// </summary>
        internal static TypeFacts Facts(Type type, bool exact = false)
        {
            int epoch = KnownSubtypes.Epoch;

            if (FactsByType.TryGetValue((type, exact), out (int Epoch, TypeFacts Facts) known) && known.Epoch == epoch)
            {
                return known.Facts;
            }

            TypeFacts facts = Scan(type, exact);

            FactsByType[(type, exact)] = (epoch, facts);

            return facts;
        }

        private static readonly ConcurrentDictionary<(Type, bool), (int Epoch, TypeFacts Facts)> FactsByType = new();

        /// <summary>
        /// Reads every type a value of this type can hold, however deep, for a field denied for Select
        /// and for a member that can hold an object of any type.
        /// </summary>
        /// <remarks>
        /// Wider than the walker on purpose. The walker stops four segments deep, does not enter a
        /// framework type and reads declared types only, so a field denied beneath any of those has no
        /// path, and resolves as allowed. This follows every property, a read-only one included, every
        /// collection, the type arguments of a framework generic such as <c>Dictionary&lt;string, T&gt;</c>,
        /// and every loaded subtype of a type it reads, a framework class's included, and remembers the
        /// types it has read rather than counting levels. A member that can hold anything is reported
        /// rather than read.
        /// </remarks>
        private static TypeFacts Scan(Type type, bool exact)
        {
            bool denies = false;
            bool holdsObject = false;
            bool holdsMembers = false;
            HashSet<Type> seen = new();
            Stack<Type> pending = new();

            void Read(Type candidate)
            {
                if (seen.Add(candidate))
                {
                    pending.Push(candidate);
                }
            }

            void Subtypes(Type of)
            {
                foreach (Type subtype in KnownSubtypes.Of(of))
                {
                    holdsMembers = true;

                    Read(subtype);
                }
            }

            void Enqueue(Type candidate, bool root)
            {
                Type peeled = AttributePolicyProvider.Peeled(candidate);

                // An application's own collection class declares members beside the elements it holds, at any
                // layer: the member's own type, or the element of a list or an array of it.
                foreach (Type layer in AttributePolicyProvider.Layers(candidate))
                {
                    if (OwnsMembers(layer))
                    {
                        holdsMembers = true;

                        Read(layer);

                        if (!(root && exact))
                        {
                            Subtypes(layer);
                        }
                    }
                }

                if (HoldsAnything(peeled))
                {
                    holdsObject = true;

                    // An application's own collection that is not generic can hold anything, and it still
                    // declares members of its own, which are read as any other type's are.
                    if (peeled.IsGenericParameter || AttributePolicyProvider.IsFramework(peeled))
                    {
                        return;
                    }
                }

                if (AttributePolicyProvider.NavigationTypeOf(candidate) is { } navigation)
                {
                    holdsMembers = true;

                    Read(navigation);

                    if (!(root && exact))
                    {
                        Subtypes(navigation);
                    }
                }
                else if (peeled.IsClass && !peeled.IsSealed && !peeled.IsGenericType && !(root && exact))
                {
                    // A framework class an application can derive from, an Exception or a Stream: the
                    // subtype a value holds is read, since the class itself carries no policy.
                    Subtypes(peeled);
                }

                if (peeled.IsGenericType && AttributePolicyProvider.IsFramework(peeled))
                {
                    foreach (Type argument in peeled.GetGenericArguments())
                    {
                        Enqueue(argument, false);
                    }
                }
            }

            Enqueue(type, true);

            while (pending.Count > 0 && !(denies && holdsObject))
            {
                foreach (PropertyInfo property in pending.Pop().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length != 0)
                    {
                        continue;
                    }

                    if (!denies && DeclaresDenial(property))
                    {
                        denies = true;
                    }

                    Enqueue(property.PropertyType, false);
                }
            }

            return new TypeFacts(denies, holdsObject, holdsMembers);
        }

        /// <summary>
        /// True for a type whose value can be an object of any type: <see cref="object"/> itself, a type
        /// parameter an open generic subtype leaves unbound, a framework interface such as
        /// <c>IComparable</c>, and a collection that is not generic, such as <c>ArrayList</c>,
        /// <c>IEnumerable</c>, <see cref="Array"/> or an application's own.
        /// </summary>
        private static bool HoldsAnything(Type peeled) =>
            peeled == typeof(object)
            || peeled == typeof(ValueType)
            || peeled.IsGenericParameter
            || (!peeled.IsGenericType
                && ((AttributePolicyProvider.IsFramework(peeled) && peeled.IsInterface)
                    || (peeled != typeof(string) && typeof(IEnumerable).IsAssignableFrom(peeled)
                                                 && !ValueCollections.Contains(peeled))));

        /// <summary>The framework's collections that are not generic yet hold values only: bits and strings.</summary>
        private static readonly HashSet<Type> ValueCollections = new()
        {
            typeof(BitArray),
            typeof(System.Collections.Specialized.StringCollection),
            typeof(System.Collections.Specialized.StringDictionary),
            typeof(System.Collections.Specialized.NameValueCollection)
        };

        /// <summary>
        /// What a type can hold that the policy cannot name a path to.
        /// </summary>
        /// <param name="DeniesSelect">A field denied for Select somewhere in what it can hold.</param>
        /// <param name="HoldsObject">A member that can hold an object of any type somewhere in what it can hold.</param>
        /// <param name="HoldsMembers">An application type's members somewhere in what it can hold.</param>
        internal readonly record struct TypeFacts(bool DeniesSelect, bool HoldsObject, bool HoldsMembers)
        {
            /// <summary>True when either fact holds, so the type cannot be shown to be free of policy.</summary>
            internal bool Opaque => DeniesSelect || HoldsObject;
        }

        /// <summary>
        /// The declared type at a path, read the way the walker reads it: through any collection, one
        /// segment at a time. Null when a segment names nothing.
        /// </summary>
        internal Type? DeclaredType(string path) => DeclaredType(_entityType, path);

        private static Type? DeclaredType(Type root, string path) => Property(root, path)?.PropertyType;

        private static PropertyInfo? Property(Type root, string path)
        {
            Type? current = root;
            PropertyInfo? property = null;

            foreach (string segment in path.Split(SegmentSeparator))
            {
                if (current is null || CacheReflection.FindProperty(current, segment) is not { } next)
                {
                    return null;
                }

                property = next;
                current = AttributePolicyProvider.NavigationTypeOf(next.PropertyType);
            }

            return property;
        }

        /// <summary>
        /// The key the projection builder would add to a node that a list of narrowed paths passes
        /// through, when this caller may not select it; null when every such key is allowed.
        /// </summary>
        /// <remarks>
        /// The builder adds the key of each nested node it builds whether the list names it or not,
        /// so a narrowing that dropped a denied key would carry it anyway.
        /// </remarks>
        internal (string Path, FieldPolicy Policy)? DeniedKey(IEnumerable<string> paths)
        {
            HashSet<string> nodes = new(StringComparer.Ordinal);

            foreach (string path in paths)
            {
                for (int cut = path.IndexOf(SegmentSeparator); cut >= 0; cut = path.IndexOf(SegmentSeparator, cut + 1))
                {
                    string node = path[..cut];

                    if (!nodes.Add(node)
                        || TypeAt(_entityType, node) is not { } type
                        || CacheReflection.FindProperty(type, "Id") is not { } key)
                    {
                        continue;
                    }

                    string keyPath = $"{node}.{key.Name}";
                    FieldPolicy policy = PolicyFor(keyPath, PolicyFeature.None);

                    if (!policy.Allows(PolicyFeature.Select))
                    {
                        return (keyPath, policy);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The first of a list of narrowed paths that the core's own validation refuses, or null when
        /// it accepts them all.
        /// </summary>
        /// <remarks>
        /// The walk above reads collections the way the policy does, and the core reads a narrower
        /// set of them: a path through an <c>IReadOnlyList&lt;T&gt;</c> is one the policy can deny and
        /// the core cannot project.
        /// </remarks>
        internal string? Unprojectable(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                try
                {
                    CacheReflection.ValidatePropertyPath(_entityType, path);
                }
                catch (LogicException)
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>The type a validated path names, or null when it names nothing.</summary>
        private Type? TypeAt(string path) => TypeAt(_entityType, path);

        private static Type? TypeAt(Type root, string path)
        {
            try
            {
                return CacheReflection.GetFieldType(root, path);
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

        /// <summary>Refuses a filter nesting its groups deeper than the budget allows.</summary>
        internal void CheckConditionDepth(int depth)
        {
            if (depth > _options.Caps.MaxConditionDepth)
            {
                Raise(Cap("MaxConditionDepth", _options.Caps.MaxConditionDepth, depth, WholeClause));
            }
        }

        /// <summary>Refuses a segment carrying more condition sets than the budget allows.</summary>
        internal void CheckConditionSetCount(int count)
        {
            if (count > _options.Caps.MaxConditionSets)
            {
                Raise(Cap("MaxConditionSets", _options.Caps.MaxConditionSets, count, WholeClause));
            }
        }

        /// <summary>Refuses a condition carrying more values than the budget allows.</summary>
        /// <remarks>
        /// An <c>In</c> is one comparison per value, so one condition can make a predicate of any size
        /// while spending one condition and one field.
        /// </remarks>
        internal void CheckConditionValues(int most)
        {
            if (most > _options.Caps.MaxConditionValues)
            {
                Raise(Cap("MaxConditionValues", _options.Caps.MaxConditionValues, most, WholeClause));
            }
        }

        /// <summary>Refuses a summary computing more aggregates than the budget allows.</summary>
        internal void CheckAggregateCount(int count)
        {
            if (count > _options.Caps.MaxAggregates)
            {
                Raise(Cap("MaxAggregates", _options.Caps.MaxAggregates, count, WholeClause));
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

            // Under the strict tier the refusal names no field: a path the caller wrote comes back
            // canonicalized, which would confirm that it names something.
            return new PolicyException(
                PolicyErrorCode.CapExceeded, IsStrict ? WholeClause : fieldPath, PolicyFeature.None, _options.Tier)
            {
                SourceOrigin = origin,
                AuditPath = fieldPath
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

                    // The strict tier names neither the scope's column nor the key it reads, which together
                    // describe how rows are partitioned. The trace above and the audit keep both.
                    throw new PolicyException(
                        PolicyErrorCode.MissingContextValue, IsStrict ? WholeClause : path, PolicyFeature.Where,
                        _options.Tier)
                    {
                        SourceOrigin = IsStrict ? null : origin,
                        AuditPath = path
                    };
                }

                values.Add(value);
            }
            else if (!predicate.IsNullCheck)
            {
                values.Add(predicate.Value!);
            }

            RecordInjected(path, predicate.Operator, Widens<T>(predicate, path));

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
        /// is the difference between a fixable error and a riddle. The trace and the audit keep the
        /// canonical path, as they do everywhere else.
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
                SourceOrigin = origin,
                AuditPath = fieldPath
            };
        }

        /// <summary>
        /// Refuses a filter inside a set operation on a field the caller may not project.
        /// </summary>
        /// <remarks>
        /// Records its own reason rather than going through <see cref="Deny"/>, because the refusal
        /// is not what the field's policy appears to say: the field is allowed for filtering, and
        /// the same filter succeeds outside a segment. Without the reason an operator reading a
        /// trace would go looking for a denial that is not there.
        /// </remarks>
        internal void DenySegmentInference(string fieldPath, FieldPolicy policy)
        {
            const string origin =
                "denied for projection, and a set operation reconstructs a field from membership " +
                "rather than from the columns it returns";

            _trace.Add(new PolicyDecision(fieldPath, PolicyFeature.Segment, PolicyAction.Denied, origin));

            if (IsDryRun)
            {
                return;
            }

            // Only ever raised under the strict tier, where a field refusal names no field and no
            // source. The origin stays in the trace, where an operator reads it.
            throw Exception(fieldPath, PolicyFeature.Segment, PolicyErrorCode.FieldDeniedForSegment, policy);
        }

        /// <summary>How many decisions the query's trace holds.</summary>
        internal int TraceCount => _trace.Count;

        /// <summary>Withdraws the decisions recorded after the first <paramref name="count"/>.</summary>
        internal void WithdrawTrace(int count) => _trace.Withdraw(count);

        /// <summary>
        /// Records that a synthesized projection left a member out whole, and why: something beneath it
        /// is denied and the member cannot be narrowed.
        /// </summary>
        internal void LeaveOut(string fieldPath, string reason) =>
            _trace.Add(new PolicyDecision(fieldPath, PolicyFeature.Select, PolicyAction.Dropped, reason));

        /// <summary>
        /// Refuses, in either tier, a projection that would have to be narrowed around a denied field
        /// and cannot be built that way.
        /// </summary>
        /// <remarks>
        /// Dropping is not an option here, for the reason it is not for a carried key: the projection
        /// that would be built is not the one that was gated, so the only way to honour the denial is
        /// to decline to build it.
        /// </remarks>
        internal void RefuseNarrowing(string fieldPath, FieldPolicy? policy, string reason)
        {
            _trace.Add(new PolicyDecision(fieldPath, PolicyFeature.Select, PolicyAction.Denied, reason));

            if (IsDryRun)
            {
                return;
            }

            throw Exception(fieldPath, PolicyFeature.Select, PolicyErrorCode.FieldDeniedForSelect, policy);
        }

        /// <summary>
        /// Applies a denial the policy cannot name a path to: a field denied for Select that a named
        /// member holds beyond the walker's depth or inside a framework collection. Throws in the strict
        /// tier, as a denied field beneath a named navigation does, and otherwise records the drop.
        /// </summary>
        /// <returns>True when the caller must narrow it away. False in a dry run.</returns>
        internal bool RefuseHidden(string fieldPath)
        {
            const string origin = "it holds a field denied for Select where no path can name it";

            _trace.Add(new PolicyDecision(
                fieldPath, PolicyFeature.Select, IsStrict ? PolicyAction.Denied : PolicyAction.Dropped, origin));

            if (IsDryRun)
            {
                return false;
            }

            if (IsStrict)
            {
                throw Exception(fieldPath, PolicyFeature.Select, PolicyErrorCode.FieldDeniedForSelect, null);
            }

            return true;
        }

        /// <summary>Records that a field of the type's default order was left out for this caller.</summary>
        internal void SkipDefaultOrder(string fieldPath, FieldPolicy policy)
        {
            string? sources = Describe(policy);

            _trace.Add(new PolicyDecision(
                fieldPath,
                PolicyFeature.Order,
                PolicyAction.Dropped,
                sources is null ? "left out of the default order" : $"left out of the default order: {sources}"));
        }

        /// <summary>Records that the library added a predicate the caller did not send.</summary>
        internal void RecordInjected(string fieldPath, Operator op, bool orNull = false) =>
            _trace.Add(new PolicyDecision(
                fieldPath,
                PolicyFeature.Where,
                PolicyAction.Injected,
                orNull ? $"forced predicate ({op}, or null)" : $"forced predicate ({op})"));

        /// <summary>Resolves one field's policy, once per query.</summary>
        internal FieldPolicy PolicyFor(string fieldPath, PolicyFeature feature)
        {
            if (_unknown.Contains(fieldPath))
            {
                return new FieldPolicy(fieldPath, DeniedEverything, Array.Empty<PolicySource>(), isSealed: true);
            }

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
                PolicyErrorCode.CapExceeded, IsStrict ? WholeClause : fieldPath, feature, _options.Tier)
            {
                SourceOrigin = origin,
                AuditPath = fieldPath
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
            string? reason = _unknown.Contains(fieldPath)
                ? $"names nothing on {_entityType.Name}"
                : Describe(policy);

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
            // Every field refusal alike, so a denied field and a name matching nothing cannot be told
            // apart by what comes back: the same code for the clause, no path, no rule, no attribute.
            if (IsStrict && IsFieldDenial(code))
            {
                // Inside a segment, one code for every field refusal. A field is refused there for taking
                // part at all or for one clause, and a name that matches nothing is refused for taking
                // part, so answering by clause would set a field the caller may not order by apart from
                // a name that does not exist.
                return InSegment
                    ? new PolicyException(
                        PolicyErrorCode.FieldDeniedForSegment, WholeClause, PolicyFeature.Segment, _options.Tier)
                    {
                        AuditPath = fieldPath
                    }
                    : new PolicyException(code, WholeClause, feature, _options.Tier) { AuditPath = fieldPath };
            }

            // A resolved policy lists the winning source for every feature it decided, not for this
            // one alone, so naming a single source is only honest when there is a single source.
            // Per-feature attribution arrives with the explain endpoint; guessing here would put a
            // wrong rule id in front of an operator diagnosing a refusal.
            PolicySource? sole = policy is { Sources.Count: 1 } ? policy.Sources[0] : null;

            // Reported under the name the caller used. Handing back the canonical path for a field
            // they only ever named by alias turns every refusal into schema disclosure, which is one
            // of the two reasons aliases exist. The trace and the audit keep the canonical path.
            return new PolicyException(code, Spoken(fieldPath), feature, _options.Tier)
            {
                RuleId = sole?.RuleId,
                SourceOrigin = sole?.Origin,
                AuditPath = fieldPath
            };
        }

        /// <summary>True for the codes that refuse a field for one feature.</summary>
        private static bool IsFieldDenial(PolicyErrorCode code) =>
            code is PolicyErrorCode.FieldDeniedForWhere
                or PolicyErrorCode.FieldDeniedForSelect
                or PolicyErrorCode.FieldDeniedForOrder
                or PolicyErrorCode.FieldDeniedForGroup
                or PolicyErrorCode.FieldDeniedForAggregate
                or PolicyErrorCode.FieldDeniedForSegment;

        /// <summary>Every feature denied, which is how an unknown name is gated under the strict tier.</summary>
        private static readonly IReadOnlyDictionary<PolicyFeature, PolicyEffect> DeniedEverything =
            new Dictionary<PolicyFeature, PolicyEffect>
            {
                [PolicyFeature.Where] = PolicyEffect.Deny,
                [PolicyFeature.Select] = PolicyEffect.Deny,
                [PolicyFeature.Order] = PolicyEffect.Deny,
                [PolicyFeature.Group] = PolicyEffect.Deny,
                [PolicyFeature.Aggregate] = PolicyEffect.Deny,
                [PolicyFeature.Segment] = PolicyEffect.Deny
            };

        /// <summary>Names every source that contributed, for the trace.</summary>
        private static string? Describe(FieldPolicy? policy) =>
            policy is null || policy.Sources.Count == 0
                ? null
                : string.Join(", ", policy.Sources);
    }
}
