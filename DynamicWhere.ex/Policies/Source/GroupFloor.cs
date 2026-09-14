using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using System.Globalization;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// The k-anonymity floor of design section 7.2: how small a group may be before it is suppressed,
/// and the column the suppression reads.
/// </summary>
/// <remarks>
/// Shared by the two halves that need it. The sanitizer decides whether to add the count, and the
/// result transformer decides which rows to drop — and both have to agree about the floor, or one
/// adds a column nobody reads or the other reads a column nobody added. One routine, called twice
/// with the same inputs.
/// </remarks>
internal static class GroupFloor
{
    /// <summary>
    /// The alias of the count the library adds for itself.
    /// </summary>
    /// <remarks>
    /// A plain identifier, because the alias is emitted verbatim into the generated projection and
    /// anything else either fails to parse or appends terms of its own. The leading underscores are
    /// what make it improbable rather than impossible for a caller to have chosen — which is why
    /// taking it is refused rather than assumed.
    /// </remarks>
    internal const string SizeAlias = "__dwGroupSize";

    /// <summary>
    /// The smallest group this summary may report.
    /// </summary>
    /// <param name="summary">The summary, after grouping has been canonicalized.</param>
    /// <param name="policy">The type's policy, which carries each field's own floor.</param>
    /// <param name="options">The posture, which carries the global floor.</param>
    /// <returns>The effective floor, or one when nothing sets one.</returns>
    /// <remarks>
    /// The largest in play, never the last. Two fields each naming a floor both mean it, and
    /// honouring the smaller would answer for a group one of them was written to protect.
    /// <para>
    /// A field's floor is read from its transform chain, which is where the attribute that obscures
    /// the value declares how small a group it will tolerate being aggregated over. A field nothing
    /// transforms sets no floor of its own and leaves the global setting to decide.
    /// </para>
    /// </remarks>
    internal static int For(Summary summary, TypePolicy policy, DwPolicyOptions options)
    {
        int floor = options.Caps.MinGroupSize;

        if (summary.GroupBy?.AggregateBy is not { } aggregates)
        {
            return floor;
        }

        foreach (AggregateBy aggregate in aggregates)
        {
            if (string.IsNullOrWhiteSpace(aggregate.Field))
            {
                continue;
            }

            if (policy.Transforms.TryGetValue(aggregate.Field!, out ValueTransform? transform)
                && transform.MinGroupSize > floor)
            {
                floor = transform.MinGroupSize;
            }
        }

        return floor;
    }

    /// <summary>
    /// Adds the count the floor reads, or returns the summary untouched when no floor applies.
    /// </summary>
    /// <param name="summary">The sanitized summary, modified in place.</param>
    /// <param name="floor">The effective floor.</param>
    /// <param name="dryRun">
    /// True to add the count without the predicate that enforces it, so the transformer can record
    /// what would have been dropped.
    /// </param>
    /// <param name="trace">Collects the record that the floor was applied.</param>
    /// <returns>True when a count was added and must be removed from the result again.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the caller has already used the reserved alias.
    /// </exception>
    /// <remarks>
    /// Added after gating, never before. This is the library counting on its own behalf, the way a
    /// forced predicate is the library filtering on the caller's — running it through the gate would
    /// check the caller's policy against a column the caller did not write, and charging it to the
    /// query budget would bill them for the library's own control.
    /// <para>
    /// <c>Count</c> takes no field, so nothing has to be chosen and nothing can be refused: the
    /// pipeline emits <c>Count()</c> over the group whatever the field says.
    /// </para>
    /// </remarks>
    internal static bool Inject(Summary summary, int floor, bool dryRun, PolicyTrace trace)
    {
        if (floor <= 1 || summary.GroupBy is null)
        {
            return false;
        }

        // The alias is the library's, everywhere it can appear. Reserving it in the aggregate list
        // alone left it usable in Having and in Orders, where the gate leaves an unrecognized name
        // untouched and the validator — which runs after this injection — then sees a legitimate
        // aggregate alias. A caller could bind to the count this control keeps for itself.
        Refuse(summary.Having);

        if (summary.Orders is not null)
        {
            foreach (OrderBy order in summary.Orders)
            {
                if (string.Equals(order.Field, SizeAlias, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(SizeAlias);
                }
            }
        }

        // A grouped summary with no aggregates is legal, and the list can genuinely be null despite
        // its property initializer: JSON carrying "aggregateBy": null overwrites the initializer,
        // which is why GroupBy.Clone already handles it and why all six readers in FilterSanitizer
        // null-check it. This routine has to as well, and then needs somewhere to put the size
        // column. Assigning here is safe because Inject only ever sees the sanitizer's deep clone.
        summary.GroupBy.AggregateBy ??= new List<AggregateBy>();

        foreach (AggregateBy existing in summary.GroupBy.AggregateBy)
        {
            if (string.Equals(existing.Alias, SizeAlias, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(SizeAlias);
            }
        }

        summary.GroupBy.AggregateBy.Add(new AggregateBy
        {
            Aggregator = Aggregator.Count,
            Alias = SizeAlias
        });

        // Filtered by the database rather than by the transformer, so the floor governs the whole
        // answer and not only the rows on the page. Suppressing after the query left TotalCount
        // and PageCount describing a result that was never returned — a count of the groups the
        // floor exists to hide — and, because suppression ran after Skip and Take, thinned each
        // page without backfilling it, so no sequence of requests ever returned the surviving
        // groups in full.
        //
        // Not in a dry run. That posture records what it would have done and does nothing, and a
        // predicate the database applied cannot be un-applied; ResultTransformer.Suppress records
        // the drops there instead.
        if (!dryRun)
        {
            ConditionGroup reached = new()
            {
                Sort = 1,
                Connector = Connector.And,
                Conditions =
                {
                    new Condition
                    {
                        Sort = 1,
                        Field = SizeAlias,
                        DataType = DataType.Number,
                        Operator = Operator.GreaterThanOrEqual,
                        Values = { floor.ToString(CultureInfo.InvariantCulture) }
                    }
                }
            };

            if (summary.Having is not null)
            {
                reached.SubConditionGroups.Add(summary.Having);
            }

            summary.Having = reached;

            // One record for the control, not one per group. An operator asking why a department
            // is missing still gets an answer that is not "look at the data", and the answer no
            // longer names the sizes of the groups the floor exists to hide — which the per-group
            // records handed back to the caller in the trace they can read.
            trace.Add(new PolicyDecision(
                SizeAlias,
                PolicyFeature.Aggregate,
                PolicyAction.Dropped,
                $"groups below the group floor of {floor} are excluded by the query"));
        }

        return true;
    }

    /// <summary>Refuses a having clause that has taken the alias this control reserves.</summary>
    private static void Refuse(ConditionGroup? group)
    {
        if (group is null)
        {
            return;
        }

        foreach (Condition condition in group.Conditions)
        {
            if (string.Equals(condition.Field, SizeAlias, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(SizeAlias);
            }
        }

        foreach (ConditionGroup sub in group.SubConditionGroups)
        {
            Refuse(sub);
        }
    }
}
